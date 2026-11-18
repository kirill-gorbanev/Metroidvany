"""gridc — компилятор и проверка сетки локации.

Читает level/<loc>/site.txt, L<k>.txt (карты уровней), dressing.txt (расстановка предметов)
и пишет level/<loc>/build.json — точную расстановку блоков для Unity (меню Fortress/Grid/Build).
Все блоки стоят на сетке 2×2 м, этаж 3 м; ничего не висит, проёмы ведут в проходимые клетки.

    python -I gridc.py fortress            # собрать + отчёт
    python -I gridc.py fortress --strict   # код возврата 1, если есть ошибки

Формат — level/FORMAT.md.
"""
import json, math, os, re, sys, glob
from collections import deque, defaultdict

CELL, STOREY = 2.0, 3.0
WALL_FRONT, WALL_BACK = 0.11, 0.09   # грани тела стены от ребра (замер лучами); до ±0.2 выступает только верхняя балка
WALL_SY = 3.0 / 3.123   # стена набора 3.12 м: сжимаем до этажа, чтобы балка не торчала и стены не перекрывались
ROOT = os.path.dirname(os.path.abspath(__file__))
CAT = json.load(open(os.path.join(ROOT, "catalog.json"), encoding="utf-8"))

OUT_CH = {".": "grass", ":": "pave", ",": "soil", "=": "deck", "_": "deck"}   # _ — мост (настил) над водой
STAIR_CH = {"^": (0, 1), "v": (0, -1), ">": (1, 0), "<": (-1, 0)}
DIRS = {"N": (0, 1), "E": (1, 0), "S": (0, -1), "W": (-1, 0)}
OPP = {"N": "S", "S": "N", "E": "W", "W": "E"}
WALL_CH = set("|-")
OPENING_CH = set("DAWwG")
PASS_CH = set(" DAGX!CVY")         # можно пройти через ребро (X — после выбивания, C — после кирки)
BARRIER_CH = set("|-DAWwBXGpPrf!CVY")
WIDE_CH = set("VY")          # широкий проём для подвод: V — ворота с решёткой, Y — проезд со створками
WATER_CH = "~L_"            # вода (у моста _ — под настилом)
PARAPET_P = 1.2    # подоконник амбразуры зубчатого парапета: выше прыжка игрока (1.1 м)
MERLON_H = 0.8     # зубец над ним: верх 2.0 м — стоящий игрок 1.8 м скрыт
FLOOR_MODELS = {"stone": "Floor_UnevenBrick", "brick": "Floor_Brick", "redbrick": "Floor_RedBrick",
                "wood": "Floor_WoodDark", "woodlight": "Floor_WoodLight"}
CEIL_MODELS = {"wood": "Floor_WoodDark", "woodlight": "Floor_WoodLight", "stone": "Floor_UnevenBrick", "brick": "Floor_Brick"}
LIGHTS = {  # модель -> (цвет, интенсивность, дальность, смещение огня в локальных координатах)
    "Torch_Metal": ((1.0, 0.6, 0.28), 3.2, 6.0, (0, 0.42, 0.3)),
    "Lantern_Wall": ((1.0, 0.74, 0.42), 2.6, 6.0, (0, 0.75, 1.0)),
    "CandleStick_Stand": ((1.0, 0.7, 0.4), 1.6, 4.5, (0, 1.32, 0)),
    "CandleStick_Triple": ((1.0, 0.7, 0.4), 1.2, 3.5, (0, 0.5, 0)),
    "CandleStick": ((1.0, 0.7, 0.4), 0.8, 2.6, (0.05, 0.2, 0)),
    "Candle_1": ((1.0, 0.7, 0.4), 0.6, 2.2, (0, 0.18, 0)),
    "Candle_2": ((1.0, 0.7, 0.4), 0.6, 2.2, (0, 0.3, 0)),
    "Chandelier": ((1.0, 0.72, 0.42), 3.0, 11.0, (0, -1.2, 0)),
}
WALL_MOUNT_DEFAULT_H = {"Torch_Metal": 1.9, "Lantern_Wall": 1.1, "Shelf_Simple": 1.5, "Shelf_Arch": 1.2,
                        "Shelf_Small_Bottles": 1.3, "Peg_Rack": 1.6, "Shield_Wooden": 1.8, "Banner_1": 2.6,
                        "Banner_2": 2.6, "Prop_Vine1": 2.9, "Prop_Vine2": 2.9, "Prop_Vine4": 2.6, "Prop_Vine5": 2.9,
                        "Prop_Vine6": 2.6, "Prop_Vine9": 2.0}


RULE_TAGS = [  # номер правила из level/ASSEMBLY_RULES.md по смыслу сообщения
    ("[", None), ("висит", "A4"), ("пол висит", "A4"), ("земля висит", "A4"), ("не помещается", "A4"),
    ("залезает в стену", "A5"), ("наезжает", "A5"), ("выше потолка", "A5"), ("крыши", "A6"), ("потолка/крыши", "A6"),
    ("выход в никуда", "C1"), ("дверь ведёт", "C1"), ("ворота ведут", "C1"), ("упирается в сплошной", "C1"),
    ("окно смотрит", "C2"), ("лестница стоит посреди", "D1"), ("длина", "D1"), ("вход ", "D1"), ("выход наверху", "D1"),
    ("над лестницей", "D1"), ("стремянк", "D2"), ("люк", "D2"), ("из воды", "D3"), ("лестнице из воды", "D3"),
    ("обрыв", "D4"), ("край проёма", "D4"), ("недостижимо", "D5"), ("не замкнута", "B3"), ("вода и комната", "B3"),
    ("вода комнаты", "B3"), ("колодец", "E1"), ("нет модели", "G1"), ("неизвестный символ", "G1"),
]


def isroom(c):
    """Клетка комнаты: строчная буква, кроме 'v' — это стрелка лестницы вниз."""
    return c.islower() and c != "v"


def rule_of(msg):
    if msg.startswith("["):
        return ""
    for key, rid in RULE_TAGS[1:]:
        if key in msg:
            return f"[{rid}] "
    return ""


class Report:
    def __init__(self):
        self.err, self.warn, self.info = [], [], []

    def e(self, where, msg): self.err.append(f"ОШИБКА  {where}: {rule_of(msg)}{msg}")
    def w(self, where, msg): self.warn.append(f"внимание {where}: {msg}")
    def i(self, msg): self.info.append(msg)


def rot2(dx, dz, yaw):
    """Поворот как в Unity вокруг Y (градусы): локальный (x,z) -> мировой."""
    a = math.radians(yaw)
    return dx * math.cos(a) + dz * math.sin(a), -dx * math.sin(a) + dz * math.cos(a)


def yaw_facing(d):
    """Угол, при котором локальный +Z смотрит в сторону d (N/E/S/W)."""
    return {"N": 0, "E": 90, "S": 180, "W": 270}[d]


# ============================================================================ parsing
class Level:
    def __init__(self, k):
        self.k = k
        self.cells, self.h, self.v = {}, {}, {}
        self.rooms = {}
        self.roofs = []
        self.bolts = []           # (i, j, dir, side): засов на ребре (dir N/E/S/W) или на люке (dir U)


def parse_kv(s):
    out = {}
    for m in re.finditer(r'(\w+)=("([^"]*)"|\S+)', s):
        out[m.group(1)] = m.group(3) if m.group(3) is not None else m.group(2)
    return out


def load_site(loc):
    d = os.path.join(ROOT, loc)
    site = {"size": None, "origin": (0.0, 0.0), "spawn": None, "name": loc}
    for ln in open(os.path.join(d, "site.txt"), encoding="utf-8"):
        ln = ln.split("#", 1)[0].strip()
        if not ln:
            continue
        p = ln.split()
        if p[0] == "size":
            site["size"] = (int(p[1]), int(p[2]))
        elif p[0] == "origin":
            site["origin"] = (float(p[1]), float(p[2]))
        elif p[0] == "spawn":
            i, j = p[2].split(",")
            site["spawn"] = (int(p[1].lstrip("Ll")), int(i), int(j))
        elif p[0] == "point":
            i, j = p[3].split(",")
            site.setdefault("points", []).append((p[1], int(p[2].lstrip("Ll")), int(i), int(j)))
        elif p[0] == "grass":
            site["grass"] = int(p[1])
        elif p[0] == "plain_solid":
            site["plain_solid"] = p[1] == "1"
        elif p[0] == "exit":       # exit <имя> L<k> i,j <N|E|S|W>: ребро, за которым игрок «вне стен»
            i, j = p[3].split(",")
            site.setdefault("exits", []).append((p[1], int(p[2].lstrip("Ll")), int(i), int(j), p[4]))
        elif p[0] == "finish":
            site["finish"] = p[1]
        elif p[0] == "name":
            site["name"] = " ".join(p[1:])
    return site


def load_levels(loc, site, rep):
    W, H = site["size"]
    levels = {}
    for f in sorted(glob.glob(os.path.join(ROOT, loc, "L*.txt"))):
        m = re.match(r"L(-?\d+)\.txt$", os.path.basename(f))
        if not m:
            continue
        k = int(m.group(1))
        L = Level(k)
        lines = open(f, encoding="utf-8").read().split("\n")
        mi = next((n for n, ln in enumerate(lines) if ln.strip() == "map"), None)
        if mi is None:
            rep.e(f"L{k}", "нет строки 'map'")
            continue
        for ln in lines[:mi]:
            s = ln.split("#", 1)[0].strip() if not ln.strip().startswith("room") else ln.strip()
            if s.startswith("room"):
                body = s.split("#", 1)[0] if '"' not in s.split("#", 1)[0] else s
                mm = re.match(r"room\s+([a-z])\s*(.*)$", body)
                if not mm:
                    rep.e(f"L{k}", f"плохая строка комнаты: {s}")
                    continue
                kv = parse_kv(mm.group(2))
                L.rooms[mm.group(1)] = {"name": kv.get("name", mm.group(1)), "floor": kv.get("floor", "stone"),
                                        "walls": kv.get("walls", "stone"), "ceiling": kv.get("ceiling", "wood"),
                                        "sky": kv.get("sky", "0") == "1", "narrow": kv.get("narrow", "0") == "1",
                                        "plain": kv.get("plain", "0") == "1", "nav": kv.get("nav", ""),
                                        "tall": kv.get("tall", "0") == "1"}
            elif s.startswith("bolt"):
                mm = re.match(r"bolt\s+(-?\d+),(-?\d+)\s+([NESWU])\s+side=([NESWUD])", s)
                if not mm:
                    rep.e(f"L{k}", f"плохая строка засова: {s} (bolt i,j <N|E|S|W|U> side=<...>)")
                    continue
                L.bolts.append((int(mm.group(1)), int(mm.group(2)), mm.group(3), mm.group(4)))
            elif s.startswith("roof") or s.startswith("gable"):
                p = s.split()
                kv = parse_kv(" ".join(p[2:]))
                at = [float(x) for x in kv.get("at", "0,0").split(",")]
                L.roofs.append({"type": p[0], "model": p[1], "at": at, "rot": float(kv.get("rot", 0)),
                                "y": float(kv.get("y", 0)), "len": float(kv["len"]) if "len" in kv else None,
                                "s": float(kv.get("s", 1)), "sy": float(kv.get("sy", kv.get("s", 1)))})
        grid = lines[mi + 1: mi + 1 + 2 * H + 1]
        if len(grid) < 2 * H + 1:
            rep.e(f"L{k}", f"в карте {len(grid)} строк, нужно {2 * H + 1}")
            grid += [""] * (2 * H + 1 - len(grid))
        for r, ln in enumerate(grid):
            ln = ln.rstrip("\r").ljust(2 * W + 1)
            if len(ln) > 2 * W + 1 and ln[2 * W + 1:].strip():
                rep.w(f"L{k} строка {r}", "длиннее ширины карты — хвост отброшен")
            for c in range(2 * W + 1):
                ch = ln[c]
                if r % 2 == 1 and c % 2 == 1:
                    L.cells[((c - 1) // 2, H - 1 - (r - 1) // 2)] = ch
                elif r % 2 == 0 and c % 2 == 1:
                    L.h[((c - 1) // 2, H - r // 2)] = ch
                elif r % 2 == 1 and c % 2 == 0:
                    L.v[(c // 2, H - 1 - (r - 1) // 2)] = ch
        for (i, j), ch in L.cells.items():
            if isroom(ch) and ch not in L.rooms:
                L.rooms[ch] = {"name": ch, "floor": "stone", "walls": "stone", "ceiling": "wood", "sky": False}
                rep.w(f"L{k}", f"комната '{ch}' без описания — камень по умолчанию")
        levels[k] = L
    return levels


# ============================================================================ model
class Site:
    def __init__(self, loc):
        self.rep = Report()
        self.loc = loc
        self.site = load_site(loc)
        self.W, self.H = self.site["size"]
        self.OX, self.OZ = self.site["origin"]
        self.levels = load_levels(loc, self.site, self.rep)
        self.kmin, self.kmax = min(self.levels), max(self.levels)
        self.pieces, self.ground, self.water, self.ladders, self.lights, self.volumes = [], [], [], [], [], []
        self.zone = {}
        self.stairs = []          # dict(k, cells, dir, entry, exit, kind)
        self.holes = {}           # (k,i,j) -> ('stair'|'ladder', exit_side or None)
        self.edge_kind = {}       # (k, 'h'|'v', a, b) -> 'single'|'double'|...
        self.props = {}
        self.ladder_side = {}
        self.copings = defaultdict(list)
        self.edge_pieces = defaultdict(list)    # (k,t,a,b) -> стены на ребре (для стыков на углах)
        self.edge_faces = defaultdict(list)     # (k,t,a,b) -> [(куда смотрит кладка, сдвиг)]
        self.trim_cells = []
        self.line_kinds = defaultdict(list)
        self.cell_room = {}
        self.stair_kind = {}
        self.stair_tops = set()
        self.slab_edges_set = set()
        self.bolt_edge = {}       # (k,t,a,b) -> (клетка со стороны засова, сторона N/E/S/W)
        self.bolt_hatch = {}      # (k,i,j) стремянки -> 'U' (засов сверху) | 'D'
        for k, L in self.levels.items():
            for (i, j, d, side) in L.bolts:
                if d == "U":
                    self.bolt_hatch[(k, i, j)] = side
                    continue
                if side not in (d, OPP[d]):
                    self.rep.e(f"L{k} засов {i},{j} {d}", f"сторона засова {side} не по обе стороны ребра {d}")
                    continue
                dx, dz = DIRS[d]
                cell = (i + dx, j + dz) if side == d else (i, j)
                self.bolt_edge[(k,) + self.edge_key(i, j, d)] = (cell, side)

    # ---- access
    def ch(self, k, i, j):
        if k < self.kmin:
            return "#"
        L = self.levels.get(k)
        if L is None or not (0 <= i < self.W and 0 <= j < self.H):
            return " "
        return L.cells.get((i, j), " ")

    def edge_key(self, i, j, d):
        if d == "N": return ("h", i, j + 1)
        if d == "S": return ("h", i, j)
        if d == "E": return ("v", i + 1, j)
        return ("v", i, j)

    def edge(self, k, i, j, d):
        L = self.levels.get(k)
        if L is None:
            return " "
        t, a, b = self.edge_key(i, j, d)
        return (L.h if t == "h" else L.v).get((a, b), " ")

    def center(self, i, j):
        return self.OX + CELL * i + CELL / 2, self.OZ + CELL * j + CELL / 2

    def zof(self, k, i, j):
        if not (0 <= i < self.W and 0 <= j < self.H):
            return "BORDER"
        return self.zone.get((k, i, j), "SOLID" if k < self.kmin else "VOID")

    def walkable(self, k, i, j):
        c = self.ch(k, i, j)
        return isroom(c) or c in OUT_CH or c in STAIR_CH or c == "H"

    def room_props(self, k, z):
        L = self.levels.get(k)
        return L.rooms.get(z) if (L and z and len(z) == 1) else None

    def put(self, model, pos, yaw=0.0, rx=0.0, scl=(1, 1, 1), cell=None, room=None, tags=None, kind=None):
        if model not in CAT:
            self.rep.e("каталог", f"нет модели {model}")
            return None
        p = {"m": model, "prefab": CAT[model]["prefab"], "pos": [round(pos[0], 4), round(pos[1], 4), round(pos[2], 4)],
             "rot": [rx, round(yaw % 360, 3), 0.0], "scl": list(scl), "kind": kind or CAT[model]["kind"]}
        if cell: p["cell"] = list(cell)
        if room: p["room"] = room
        if tags: p["tags"] = tags
        self.pieces.append(p)
        return p

    # ============================================================== zones
    def infer_zones(self):
        rep = self.rep
        for k, L in self.levels.items():
            for (i, j), c in L.cells.items():
                if isroom(c): z = c
                elif c in OUT_CH: z = "OUT"
                elif c == "#": z = "SOLID"
                elif c in "~L": z = "WATER"
                elif c == " ": z = "VOID"
                elif c in STAIR_CH or c in "HO": z = None
                else:
                    rep.e(f"L{k} {i},{j}", f"неизвестный символ клетки '{c}'")
                    z = "VOID"
                self.zone[(k, i, j)] = z
        # лестницы: пробежки одинаковых стрелок
        seen = set()
        for k, L in self.levels.items():
            for (i, j), c in sorted(L.cells.items()):
                if c not in STAIR_CH or (k, i, j) in seen:
                    continue
                dx, dz = STAIR_CH[c]
                si, sj = i, j
                while self.ch(k, si - dx, sj - dz) == c:
                    si, sj = si - dx, sj - dz
                run = []
                ci, cj = si, sj
                while self.ch(k, ci, cj) == c:
                    run.append((ci, cj)); seen.add((k, ci, cj)); ci, cj = ci + dx, cj + dz
                d = next(n for n, v in DIRS.items() if v == (dx, dz))
                entry = (si - dx, sj - dz)
                exit_ = (ci, cj)
                where = f"L{k} лестница {si},{sj}"
                if len(run) not in (2, 3):
                    rep.e(where, f"длина {len(run)} клеток: деревянная лестница — 2 клетки, каменная — 3")
                ez = self.zone.get((k,) + entry)
                if ez in (None, "VOID", "SOLID", "WATER"):
                    rep.e(where, f"вход {entry} не проходимый ('{self.ch(k, *entry)}')")
                    ez = "OUT"
                for (a, b) in run:
                    self.zone[(k, a, b)] = ez
                if not self.walkable(k + 1, *exit_):
                    rep.e(where, f"выход наверху L{k + 1} {exit_} не проходимый ('{self.ch(k + 1, *exit_)}')")
                for (a, b) in run:
                    if self.ch(k + 1, a, b) != " ":
                        rep.e(where, f"над лестницей L{k + 1} {a},{b} должна быть пустота ' ' (проём), а там '{self.ch(k + 1, a, b)}'")
                    self.holes[(k + 1, a, b)] = ("stair", d if (a, b) == run[-1] else None)
                kind = "wood" if len(run) == 2 else "stone"
                # лестница прижата к стене хотя бы одним боком (в доме марш посреди комнаты — так не строят)
                sides = [s for s in DIRS if DIRS[s][0] * dx + DIRS[s][1] * dz == 0]
                def walled(s):
                    for (a, b) in run:
                        e = self.edge(k, a, b, s)
                        nx, nz = a + DIRS[s][0], b + DIRS[s][1]
                        if not (e in WALL_CH or e in OPENING_CH or e in "pP" or self.zone.get((k, nx, nz)) == "SOLID"
                                or self.ch(k, nx, nz) == "#"):
                            return False
                    return True
                for s in sides:
                    if walled(s):
                        for (a, b) in run:
                            if self.edge(k, a, b, s) in "WwDA":
                                rep.e(where, f"[D1] марш закрывает {'окно' if self.edge(k, a, b, s) in 'Ww' else 'проём'} в стене у клетки {a},{b} — перенеси окно или лестницу")
                if not any(walled(s) for s in sides):
                    msg = "лестница стоит посреди помещения — прижми её боком к стене (вдоль стены или в угол)"
                    (rep.e if kind == "wood" else rep.w)(where, msg)
                self.stairs.append({"k": k, "run": run, "dir": d, "entry": entry, "exit": exit_, "kind": kind, "zone": ez})
                for (a, b) in run:
                    self.stair_kind[(k, a, b)] = kind
                self.stair_tops.add((k, run[-1], exit_))
        # H, O, L — по соседям через открытое ребро
        for k, L in self.levels.items():
            for (i, j), c in L.cells.items():
                if c not in "HO":
                    continue
                zs = []
                for d, (dx, dz) in DIRS.items():
                    if self.edge(k, i, j, d) in PASS_CH:
                        z = self.zone.get((k, i + dx, j + dz))
                        if z and z not in ("VOID", "SOLID", "WATER"):
                            zs.append(z)
                if not zs and c == "H" and (self.ch(k - 1, i, j) == "H" or self.ch(k + 1, i, j) == "H"):
                    self.zone[(k, i, j)] = "SHAFT"   # шахта (дымоход, колодец со скобами): глухие стены вокруг
                elif not zs:
                    rep.e(f"L{k} {i},{j}", f"'{c}' не примыкает ни к одной проходимой клетке через открытое ребро")
                    self.zone[(k, i, j)] = "OUT"
                else:
                    self.zone[(k, i, j)] = max(set(zs), key=zs.count)
        # люки стремянок H: сторона стены и выход наверху
        self.hole_room = {}
        for k in sorted(self.levels, reverse=True):
            L = self.levels[k]
            for (i, j), c in L.cells.items():
                if c != "H":
                    continue
                if self.ch(k + 1, i, j) == "H":
                    up = self.ladder_side.get((k + 1, i, j))
                    side = up or next((d for d, (dx, dz) in DIRS.items()
                                       if self.edge(k, i, j, d) in WALL_CH or self.zone.get((k, i + dx, j + dz)) == "SOLID"), None)
                    self.ladder_side[(k, i, j)] = side
                    continue
                side = None
                for d, (dx, dz) in DIRS.items():
                    if self.edge(k, i, j, d) in WALL_CH or self.zone.get((k, i + dx, j + dz)) == "SOLID":
                        if self.edge(k + 1, i, j, d) == " " and self.walkable(k + 1, i + dx, j + dz):
                            side = d
                            break
                self.ladder_side[(k, i, j)] = side
                if side:
                    self.holes[(k + 1, i, j)] = ("ladder", side)
                    dx, dz = DIRS[side]
                    self.hole_room[(k + 1, i, j)] = self.zone.get((k + 1, i + dx, j + dz))
        for s in self.stairs:
            for (a, b) in s["run"]:
                self.hole_room[(s["k"] + 1, a, b)] = self.zone.get((s["k"] + 1,) + s["exit"])
        # двухсветные комнаты (tall=1): пустые клетки ' ' прямо над комнатой — её второй свет (стены и окна — её)
        for k, L in self.levels.items():
            for (i, j), c in L.cells.items():
                rp = L.rooms.get(c) if isroom(c) else None
                if rp and rp.get("tall") and self.ch(k + 1, i, j) == " " and (k + 1, i, j) not in self.holes:
                    self.holes[(k + 1, i, j)] = ("tall", None)
                    self.hole_room[(k + 1, i, j)] = c
        # вода внутри комнаты принадлежит комнате
        self.water_room = {}
        for k, L in self.levels.items():
            for (i, j), c in L.cells.items():
                if c not in "~L":
                    continue
                for d, (dx, dz) in DIRS.items():
                    z = self.zone.get((k, i + dx, j + dz))
                    if z and len(z) == 1 and self.edge(k, i, j, d) == " ":
                        self.water_room[(k, i, j)] = z

    def side_want(self, k, i, j):
        """Чем облицевать сторону стены, обращённую к клетке: brick | plaster | hidden."""
        z = self.zof(k, i, j)
        if (k, i, j) in self.holes and self.hole_room.get((k, i, j)):
            z = self.hole_room[(k, i, j)]          # проём лестницы/люка — часть комнаты
        if z == "SOLID":
            return "hidden"
        if z == "WATER":
            z = self.water_room.get((k, i, j), "OUT")
        if z in ("OUT", "VOID", "WATER", "BORDER", "SHAFT"):
            return "brick"
        rp = self.room_props(k, z)
        return "plaster" if rp and rp["walls"] == "plaster" else "brick"

    # ============================================================== edges
    def all_edges(self, k):
        for i in range(self.W):
            for j in range(self.H + 1):
                yield ("h", i, j)
        for i in range(self.W + 1):
            for j in range(self.H):
                yield ("v", i, j)

    def edge_cells(self, t, a, b):
        """Две клетки по сторонам ребра: (первая, вторая, сторона второй относительно первой)."""
        if t == "h":
            return (a, b - 1), (a, b), "N"
        return (a - 1, b), (a, b), "E"

    def edge_center(self, t, a, b):
        if t == "h":
            return self.OX + CELL * a + CELL / 2, self.OZ + CELL * b
        return self.OX + CELL * a, self.OZ + CELL * b + CELL / 2

    def check_and_build_edges(self):
        rep = self.rep
        self.wall_lines = defaultdict(list)    # k -> [(x0,z0,x1,z1,half_thickness)]
        self.retaining = defaultdict(dict)     # k -> {(t,a,b): сдвиг стенки}
        self.vertex_walls = defaultdict(list)  # (k,vx,vz) -> [(dir, thick)]
        for k, L in self.levels.items():
            y = STOREY * k
            for (t, a, b) in self.all_edges(k):
                c = (L.h if t == "h" else L.v).get((a, b), " ")
                P, Q, dq = self.edge_cells(t, a, b)
                zp, zq = self.zof(k, *P), self.zof(k, *Q)
                cp, cq = self.ch(k, *P), self.ch(k, *Q)
                where = f"L{k} ребро {t}{a},{b} между {P}'{cp}' и {Q}'{cq}'"
                ex, ez = self.edge_center(t, a, b)
                # вода: подпорная стенка от дна до уровня
                wp, wq = cp in WATER_CH, cq in WATER_CH
                if wp != wq:
                    face = OPP[dq] if wp else dq          # кирпич смотрит в воду
                    land = Q if wp else P
                    land_ok = (self.walkable(k, *land) and self.zof(k, *land) != "SOLID") or                         (self.zof(k, *land) == "BORDER" and k == 0)      # луг за краем карты
                    # подпорная стенка целиком в берегу: лицо кладки ровно по краю клетки воды
                    # под сплошным массивом (башня, куртина стоят в воде) стенка вровень с его гранью — кладка до дна
                    # без уступа (иначе массив «висит» над водой); под берегом — целиком в берегу
                    rs = 0.0 if (self.site.get("plain_solid") and self.zof(k, *land) == "SOLID") else -0.2
                    if self.site.get("plain_solid"):
                        self.slab_piece(t, ex, y - STOREY, ez, face, rs, STOREY, (k, a, b), "retaining")
                    else:
                        self.wall_piece("Wall_UnevenBrick_Straight", ex, y - STOREY, ez, face, rs, (k, a, b), "retaining")
                    self.retaining[k][(t, a, b)] = rs
                    wcell = P if wp else Q
                    if c == " " and land_ok and self.ch(k, *wcell) != "_":
                        self.copings[k].append((t, a, b, OPP[face]))   # бордюр по верху стенки (под мостом — нет), стыки — после
                    self.add_wall_line(k, t, a, b, 0.2, low=True)
                if c == " ":
                    self.check_open_edge(k, t, a, b, P, Q, zp, zq, cp, cq, where)
                    # грань сплошного массива
                    if (zp == "SOLID") != (zq == "SOLID") and "BORDER" not in (zp, zq) and not (
                            # верх каменного марша упирается в массив: марш закрывает грань от пола до верха, стенка не нужна
                            ((k, Q, P) in self.stair_tops and self.stair_kind.get((k,) + Q) == "stone") or
                            ((k, P, Q) in self.stair_tops and self.stair_kind.get((k,) + P) == "stone")):
                        other = Q if zp == "SOLID" else P
                        face = dq if zp == "SOLID" else OPP[dq]
                        oc = self.ch(k, *other)
                        # верх массива вровень с полом над ним, если над массивом проходимо/пусто
                        sc = P if zp == "SOLID" else Q
                        # если над этой гранью стены нет (снаружи земля/мощение) — верхняя балка уходит под поверхность
                        above_edge = (self.levels[k + 1].h if t == "h" else self.levels[k + 1].v).get((a, b), " ") if (k + 1) in self.levels else " "
                        yy = y - 0.14 if above_edge in " !" and self.ch(k + 1, *sc) != "#" else y
                        if self.site.get("plain_solid") and self.zof(k, *other) in ("OUT", "VOID", "WATER"):
                            # наружная грань массива (куртина, основания башен, стенки ямы): плита без пояса, до пола выше
                            self.slab_piece(t, ex, y, ez, face, 0.0, STOREY, (k, a, b), "solid", edge=(k, t, a, b))
                        elif self.side_want(k, *other) == "plaster":
                            self.wall_piece("Wall_UnevenBrick_Straight", ex, yy, ez, OPP[face], 0.0, (k, a, b), "solid", edge=(k, t, a, b))
                        else:
                            self.wall_piece("Wall_UnevenBrick_Straight", ex, yy, ez, face, 0.0, (k, a, b), "solid", edge=(k, t, a, b))
                        self.add_wall_line(k, t, a, b, 0.11)
                    continue
                if c not in BARRIER_CH:
                    rep.e(where, f"неизвестный символ ребра '{c}'")
                    continue
                if c == "C":
                    n0 = len(self.pieces)
                    self.build_wall(k, t, a, b, "-" if t == "h" else "|", P, Q, dq, zp, zq, where)
                    for pc in self.pieces[n0:]:
                        pc["tags"] = pc.get("tags", []) + ["crack"]
                    self.door_edges.append((k, t, a, b))
                    for cell_ in (P, Q):
                        if not self.walkable(k, *cell_):
                            rep.e(where, f"трещина ведёт в непроходимую клетку '{self.ch(k, *cell_)}'")
                elif c in WIDE_CH:
                    self.build_wide(k, t, a, b, c, P, Q, dq, zp, zq, where)
                elif c in WALL_CH or c in OPENING_CH:
                    self.build_wall(k, t, a, b, c, P, Q, dq, zp, zq, where)
                elif c in "BX":
                    yaw = 0 if t == "h" else 90
                    bt = self.bolt_tags(k, t, a, b)
                    self.put("Prop_MetalFence_Simple", (ex, y, ez), yaw, scl=(1.0, 0.95, 1.0), cell=(a, b, k),
                             tags=(["bars", "gate"] + bt if bt else ["bars", "breakable"]) if c == "X" else ["bars"], kind="Opening")
                    self.add_wall_line(k, t, a, b, 0.1, kind="bars")
                    for side, z in ((P, zp), (Q, zq)):
                        if z in ("VOID", "SOLID") and c == "X":
                            rep.e(where, "решётку некуда выбить: с одной стороны пустота/массив")
                elif c in "pP":
                    yaw = 90 if t == "h" else 0
                    sill = PARAPET_P if c == "P" else 1.0
                    self.put("Stairs_Exterior_SingleSideThick", (ex, y, ez), yaw, scl=(1.0, sill, 1.0), cell=(a, b, k), kind="Wall",
                             tags=["parapet"])
                    if c == "P":    # зубец той же кладкой: 0.4 × 0.8 × 1.0 м по середине ребра, верх 2.0 м
                        self.put("Stairs_Exterior_SingleSideThick", (ex, y + sill, ez), yaw, scl=(1.0, MERLON_H, 0.5), cell=(a, b, k),
                                 kind="Wall", tags=["merlon"])
                    self.add_wall_line(k, t, a, b, 0.2, low=True, kind="parapet_m" if c == "P" else "parapet")
                elif c in "rf":
                    yaw = 0 if t == "h" else 90
                    self.put("Prop_WoodenFence_Single", (ex, y, ez), yaw, cell=(a, b, k), kind="Trim", tags=["railing"])
                    self.add_wall_line(k, t, a, b, 0.08, low=True)
                elif c == "!":
                    pass
            self.retaining_corners(k)
            self.corner_pillars(k)
            self.build_copings(k)

    def build_copings(self, k):
        """Бордюр по краю воды: плиты 2 м встык по верху подпорной стенки (0..0.7 м в берег).
        Внутренний угол воды — угловой камень, внешний — подрезка поперечной плиты."""
        y = STOREY * k
        strips = []
        for (t, a, b, land) in self.copings[k]:
            v0, v1 = ((a, b), (a + 1, b)) if t == "h" else ((a, b), (a, b + 1))
            strips.append({"t": t, "a": a, "b": b, "land": land, "v": (v0, v1), "cut": [0.0, 0.0]})
        at = defaultdict(list)
        for s in strips:
            for end, v in enumerate(s["v"]):
                at[v].append((s, end))
        for v, lst in at.items():
            xs = [(s, e) for s, e in lst if s["t"] == "h"]
            zs = [(s, e) for s, e in lst if s["t"] == "v"]
            if len(xs) != 1 or len(zs) != 1:
                continue
            (A, ea), (B, eb) = xs[0], zs[0]
            nA, nB = DIRS[A["land"]], DIRS[B["land"]]
            sA = 1 if ea == 0 else -1          # куда идёт плита A от узла по x
            sB = 1 if eb == 0 else -1          # куда идёт плита B от узла по z
            vx, vz = self.OX + CELL * v[0], self.OZ + CELL * v[1]
            if sB == -nA[1] and sA == -nB[0]:
                # внутренний угол воды: пустой квадрат 0.7×0.7 в берегу — угловой камень
                # угловой камень (локально 0..0.7 по x и z) поворотом кладётся в нужную четверть
                yaw = {(1, 1): 0, (1, -1): 90, (-1, -1): 180, (-1, 1): 270}[(nB[0], nA[1])]
                self.put("Prop_ExteriorBorder_Corner", (vx, y, vz), yaw, cell=(v[0], v[1], k), kind="Trim", tags=["coping"])
            elif sB == nA[1] and sA == nB[0]:
                B["cut"][eb] = 0.7                # внешний угол: поперечная плита короче на ширину бордюра
        for s in strips:
            ex, ez = self.edge_center(s["t"], s["a"], s["b"])
            L = CELL - s["cut"][0] - s["cut"][1]
            shift = (s["cut"][0] - s["cut"][1]) / 2          # вдоль ребра от первого узла ко второму
            tx, tz = (1, 0) if s["t"] == "h" else (0, 1)
            self.put("Prop_ExteriorBorder_Straight1", (ex + tx * shift, y, ez + tz * shift), yaw_facing(s["land"]),
                     scl=(L / CELL, 1, 1), cell=(s["a"], s["b"], k), kind="Trim", tags=["coping"])

    def check_open_edge(self, k, t, a, b, P, Q, zp, zq, cp, cq, where):
        rep = self.rep
        if zp == zq:
            return
        if "SOLID" in (zp, zq) or "BORDER" in (zp, zq):
            return
        walk_p, walk_q = self.walkable(k, *P) or cp == "O", self.walkable(k, *Q) or cq == "O"
        # выход из проёма лестницы/люка
        for hc, oc, side in ((P, Q, "N" if t == "h" else "E"), (Q, P, "S" if t == "h" else "W")):
            h = self.holes.get((k,) + hc)
            if h and self.walkable(k, *oc):
                if h[0] == "ladder" or h[1] == side:
                    return
        if (k, P, Q) in self.stair_tops or (k, Q, P) in self.stair_tops:
            return
        if (zp == "VOID") != (zq == "VOID"):
            if (walk_p or walk_q):
                hole = (k,) + (P if zp == "VOID" else Q)
                if hole in self.holes:
                    rep.e(where, "край проёма лестницы/люка без ограждения: поставь 'r' (перила) или стену")
                else:
                    rep.e(where, "обрыв в пустоту без ограждения: поставь 'r'/'p'/стену, или '!' если прыжок задуман")
            return
        if (zp == "WATER") or (zq == "WATER"):
            other = zq if zp == "WATER" else zp
            wc = P if zp == "WATER" else Q
            wr = self.water_room.get((k,) + wc)
            if len(other) == 1 and wr != other:
                rep.e(where, "вода и комната без стены")
            elif wr and other in ("OUT", "VOID") :
                rep.e(where, f"вода комнаты '{wr}' открыта наружу: нужна стена")
            return
        if len(zp) == 1 or len(zq) == 1:
            rep.e(where, f"граница '{zp}'/'{zq}' открыта — комната не замкнута: нужна стена или проём (D/A)")

    def add_wall_line(self, k, t, a, b, half, low=False, kind="wall"):
        x, z = self.edge_center(t, a, b)
        if t == "h":
            self.wall_lines[k].append((x - 1, z - half, x + 1, z + half, low))
        else:
            self.wall_lines[k].append((x - half, z - 1, x + half, z + 1, low))
        self.line_kinds[k].append(kind)

    def slab_piece(self, t, x, y, z, face, shift, height, cell, tag, edge=None):
        """Плита кладки вместо стены набора (у неё нет деревянного пояса): тело [shift-0.09, shift+0.11] по нормали face.
        Кладка по мировым координатам, поэтому плита и соседние стены набора сходятся без шва."""
        dx, dz = DIRS[face]
        c = shift + (WALL_FRONT - WALL_BACK) / 2
        thick = WALL_FRONT + WALL_BACK
        scl = (CELL, height, thick) if t == "h" else (thick, height, CELL)
        p = self.put("Corner_Filler", (x + dx * c, y, z + dz * c), 0, scl=scl, cell=cell, kind="Wall", tags=[tag, "slab"])
        if p is not None and edge is not None:
            self.edge_pieces[edge].append(p)
            self.edge_faces[edge].append((face, shift))
            self.slab_edges_set.add(edge)
        return p

    def wall_piece(self, model, x, y, z, face, shift, cell, tag, room=None, edge=None):
        dx, dz = DIRS[face]
        p = self.put(model, (x + dx * shift, y, z + dz * shift), yaw_facing(face), scl=(1, WALL_SY, 1), cell=cell,
                     room=room, tags=[tag] if tag else None)
        if p is not None and edge is not None:
            self.edge_pieces[edge].append(p)
            self.edge_faces[edge].append((face, shift))
        return p

    def build_wall(self, k, t, a, b, c, P, Q, dq, zp, zq, where):
        rep = self.rep
        y = STOREY * k
        ex, ez = self.edge_center(t, a, b)
        sp, sq = self.side_want(k, *P), self.side_want(k, *Q)
        if c in OPENING_CH:
            for z, cc in ((zp, self.ch(k, *P)), (zq, self.ch(k, *Q))):
                if z == "SOLID":
                    rep.e(where, f"проём '{c}' упирается в сплошной массив — выход в никуда")
            if c == "G":
                for z, cc in ((zp, self.ch(k, *P)), (zq, self.ch(k, *Q))):
                    if z in ("VOID", "SOLID", "WATER"):
                        rep.e(where, f"ворота ведут в '{cc}'")
            if c in "DAG":
                for cell, z in ((P, zp), (Q, zq)):
                    if not (self.walkable(k, *cell) or self.ch(k, *cell) in "~L"):
                        rep.e(where, f"дверь ведёт в непроходимую клетку '{self.ch(k, *cell)}'")
            if c in "Ww" and not ({zp, zq} & {"OUT", "VOID", "WATER"}):
                rep.w(where, "окно смотрит не наружу, а в соседнее помещение")
        variant = {"|": "Straight", "-": "Straight", "D": "Door_Round", "A": "Door_Round", "G": "Door_Round",
                   "W": "Window_Wide_Round", "w": "Window_Thin_Round"}[c]
        sides = (sp, sq)
        if sp == "hidden" and sq == "hidden":
            return
        if "hidden" in sides:
            other = dq if sp == "hidden" else OPP[dq]
            want = sq if sp == "hidden" else sp
            face = other if want == "brick" else OPP[other]
            pieces = [("Wall_UnevenBrick_" + variant, face, 0.0)]
            half = 0.2
        elif sp == "brick" and sq == "brick":
            ext = [d for z, d in ((zp, OPP[dq]), (zq, dq)) if z in ("OUT", "VOID", "WATER", "BORDER")]
            if len(ext) == 1:
                o = ext[0]
                pieces = [("Wall_UnevenBrick_" + variant, o, 0.0), ("Wall_UnevenBrick_" + variant, OPP[o], 2 * WALL_BACK)]
            else:
                pieces = [("Wall_UnevenBrick_" + variant, dq, WALL_BACK), ("Wall_UnevenBrick_" + variant, OPP[dq], WALL_BACK)]
            half = 0.4
        elif sp == "plaster" and sq == "plaster":
            pv = {"Straight": "Straight", "Door_Round": "Door_Round", "Window_Wide_Round": "Window_Wide_Round",
                  "Window_Thin_Round": "Window_Thin_Round"}[variant]
            pieces = [("Wall_Plaster_" + pv, dq, 0.0)]
            half = 0.2
        else:
            face = dq if sq == "brick" else OPP[dq]
            pieces = [("Wall_UnevenBrick_" + variant, face, 0.0)]
            half = 0.2
        # plain=1: наружная грань комнаты — кладка без деревянного пояса; внутренняя половина — материал комнаты
        def eff(c, z):     # проём лестницы/люка считается частью своей комнаты
            return self.hole_room.get((k,) + c) if (k,) + c in self.holes and self.hole_room.get((k,) + c) else z
        ep, eq = eff(P, zp), eff(Q, zq)
        EXT = ("OUT", "VOID", "WATER")
        rp_, rq_ = self.room_props(k, ep), self.room_props(k, eq)
        plain_room = (((rp_ and rp_.get("plain")) or ep == "SHAFT") and eq in EXT) or (((rq_ and rq_.get("plain")) or eq == "SHAFT") and ep in EXT)
        zp, zq = ep, eq
        if plain_room and "hidden" not in sides:
            o = OPP[dq] if zp in ("OUT", "VOID", "WATER") else dq          # сторона наружу
            inner_side = sq if o == OPP[dq] else sp
            pv = variant
            inner = ("Wall_Plaster_" + pv) if inner_side == "plaster" else ("Wall_UnevenBrick_" + pv)
            pieces = [("Wall_UnevenBrick_" + variant, o, 0.0, "ext"), (inner, OPP[o], 2 * WALL_BACK, None)]
            half = 0.4
        pieces = [p if len(p) == 4 else p + (None,) for p in pieces]
        outer = None  # сторона наружу (для стекла/ставен/решётки)
        for z, d in ((zp, OPP[dq]), (zq, dq)):
            if z in ("OUT", "VOID", "WATER"):
                outer = d
        for model, face, shift, mark in pieces:
            if mark == "ext" and variant == "Straight":
                self.slab_piece(t, ex, y, ez, face, shift, STOREY, (a, b, k), "ext", edge=(k, t, a, b))
            else:
                p_ = self.wall_piece(model, ex, y, ez, face, shift, (a, b, k), "wall", edge=(k, t, a, b))
                if p_ is not None and mark:
                    p_["tags"] = p_.get("tags", []) + [mark]
        S = (1, WALL_SY, 1)
        for model, face, shift, _mark in pieces:
            dx, dz = DIRS[face]
            bx, bz = ex + dx * shift, ez + dz * shift
            yaw = yaw_facing(face)
            is_outer = outer is not None and face == outer
            if c in "DAG":
                # коробка закрывает торцы проёма (у стен набора откосов нет)
                fm = "DoorFrame_Round_Brick" if self.side_want(k, *(Q if face == dq else P)) != "plaster" else "DoorFrame_Round_WoodDark"
                self.put(fm, (bx, y, bz), yaw, scl=S, cell=(a, b, k), kind="Opening", tags=["frame"])
            elif c == "W":
                self.put("Window_Wide_Round1", (bx, y, bz), yaw, scl=S, cell=(a, b, k), kind="Opening", tags=["glass"])
            elif c == "w":
                self.put("Window_Thin_Round1", (bx, y, bz), yaw, scl=S, cell=(a, b, k), kind="Opening", tags=["glass"])
                if is_outer or (outer is None and face == pieces[0][1]):
                    ox, oz = rot2(-0.05, 0.235, yaw)    # решётка вплотную к раме окна (её лицо +0.202)
                    self.put("Prop_MetalFence_Simple", (bx + ox, y + 1.0 * WALL_SY, bz + oz), yaw, scl=(0.36, 0.54, 0.4),
                             cell=(a, b, k), kind="Opening", tags=["bars"])
        # середина тела стены по нормали (для створки и решётки ворот): у наружной двойной она смещена внутрь
        ref = pieces[0][1]
        lo, hi = 9.0, -9.0
        for _m, f, s, _x in pieces:
            a0, b0 = s - WALL_BACK, s + WALL_FRONT
            if f != ref:
                a0, b0 = -b0, -a0
            lo, hi = min(lo, a0), max(hi, b0)
        mid = (lo + hi) / 2
        rdx, rdz = DIRS[ref]
        if c == "D":
            # створка: петля у края коробки, полотно по центру проёма и по центру толщины стены
            yaw = yaw_facing(ref)
            hx, hz = rot2(0.51, 0.0, yaw)
            self.put("Door_8_Round", (ex + hx + rdx * mid, y, ez + hz + rdz * mid), yaw, scl=S, cell=(a, b, k), kind="Opening",
                     tags=["door"] + self.bolt_tags(k, t, a, b))
        if c == "G":
            # подъёмная решётка в толще стены, по размеру арки (проём 1.6 x 2.4 м)
            yaw = yaw_facing(ref)
            self.put("Prop_MetalFence_Ornament", (ex + rdx * mid, y, ez + rdz * mid), yaw, scl=(0.74, 0.82, 1.0),
                     cell=(a, b, k), kind="Opening", tags=["portcullis"])
        self.add_wall_line(k, t, a, b, 0.11 if half <= 0.21 else 0.21)
        if c in "DAG":
            self.door_edges.append((k, t, a, b))

    def retaining_corners(self, k):
        """Внутренний угол рва (вода в углу, с двух сторон берег/массив): тела двух подпорных стенок не перекрываются,
        в углу щель насквозь — видно небо. Блоки кладки продолжают обе стенки до тела соседней."""
        y = STOREY * k
        rs_of = self.retaining.get(k, {})
        for (i, j), c in self.levels[k].cells.items():
            if c not in WATER_CH:
                continue
            for sx in (0, 1):
                for sz in (0, 1):
                    eh, ev = ("h", i, j + sz), ("v", i + sx, j)
                    if eh not in rs_of or ev not in rs_of:
                        continue
                    di, dj = (-1 if sx == 0 else 1), (-1 if sz == 0 else 1)
                    if self.ch(k, i + di, j + dj) in WATER_CH:
                        continue
                    vx, vz = self.OX + CELL * (i + sx), self.OZ + CELL * (j + sz)
                    nx, nz = -di, -dj                     # от угла внутрь клетки воды
                    rh, rv = rs_of[eh], rs_of[ev]
                    # u — по x в воду, w — по z в воду; тело h-стенки w∈[rh-0.09, rh+0.11], v-стенки u∈[rv-0.09, rv+0.11]
                    for (u0, u1, w0, w1) in ((rv - WALL_BACK, 0.0, rh - WALL_BACK, rh + WALL_FRONT),
                                             (rv - WALL_BACK, rv + WALL_FRONT, rh - WALL_BACK, 0.0)):
                        if u1 - u0 < 0.005 or w1 - w0 < 0.005:
                            continue
                        xa, xb = sorted((vx + nx * u0, vx + nx * u1))
                        za, zb = sorted((vz + nz * w0, vz + nz * w1))
                        self.put("Corner_Filler", ((xa + xb) / 2, y - STOREY, (za + zb) / 2), 0, scl=(xb - xa, STOREY, zb - za),
                                 cell=(i, j, k), kind="Wall", tags=["retaining", "slab", "corner"])

    def build_wide(self, k, t, a, b, c, P, Q, dq, zp, zq, where):
        """Широкий проём V/Y (ворота и проезд для подвод): подряд идущие рёбра с той же буквой на одной линии — один
        проём. Просвет 4 м (у пары рёбер — 3.7 м), высота 2.8 м: каменные косяки по краям, перемычка сверху.
        Створки — по два полотна Door_8_Flat, распахнуты внутрь (в комнату); V — ещё подъёмная решётка в толще стены."""
        rep = self.rep
        E_ = self.levels[k].h if t == "h" else self.levels[k].v
        key = (lambda s: (s, b)) if t == "h" else (lambda s: (a, s))
        s0 = a if t == "h" else b
        if E_.get(key(s0 - 1)) == c:
            return                                   # проём строится один раз — от первого ребра серии
        s1 = s0
        while E_.get(key(s1 + 1)) == c:
            s1 += 1
        for z_, cc in ((zp, self.ch(k, *P)), (zq, self.ch(k, *Q))):
            if z_ in ("VOID", "SOLID", "WATER"):
                rep.e(where, f"ворота ведут в '{cc}'")
        inward = dq if (zq and len(zq) == 1) or not (zp and len(zp) == 1) else OPP[dq]
        y = STOREY * k
        if t == "h":
            O = (self.OX + CELL * s0, self.OZ + CELL * b); T = (1.0, 0.0)
        else:
            O = (self.OX + CELL * a, self.OZ + CELL * s0); T = (0.0, 1.0)
        N = DIRS[inward]
        Lr = CELL * (s1 - s0 + 1)
        W = 4.0 if Lr >= 5.0 else Lr - 0.3
        jm = (Lr - W) / 2
        H = 2.8
        cellr = (a, b, k)

        def at(s, d):
            return O[0] + T[0] * s + N[0] * d, O[1] + T[1] * s + N[1] * d

        def box(sa, sb, da, db, y0, y1, tag):
            cx, cz = at((sa + sb) / 2, (da + db) / 2)
            ls, ld = sb - sa, db - da
            scl = (ls, y1 - y0, ld) if t == "h" else (ld, y1 - y0, ls)
            self.put("Corner_Filler", (cx, y0, cz), 0, scl=scl, cell=cellr, kind="Wall", tags=[tag, "slab", "wide"])

        TH = 0.3
        box(-0.15, jm, -TH, TH, y, y + STOREY, "jamb")
        box(Lr - jm, Lr + 0.15, -TH, TH, y, y + STOREY, "jamb")
        box(jm, Lr - jm, -TH, TH, y + H, y + STOREY, "lintel")
        # створки: полотно Door_8_Flat — петля в локальном x=0, доски к -x; -x локальный -> внутрь (N)
        yaw = {(0, 1): 90, (0, -1): 270, (1, 0): 180, (-1, 0): 0}[N]
        half = W / 2
        nsub = max(1, math.ceil(half / 1.12 - 1e-6))
        sw = half / nsub
        for sh in (jm + 0.12, Lr - jm - 0.12):
            for m in range(nsub):
                px, pz = at(sh, TH + 0.02 + m * sw)
                self.put("Door_8_Flat", (px, y, pz), yaw, scl=(sw / 1.118, (H - 0.05) / 2.1, 1.0), cell=cellr,
                         kind="Opening", tags=["gate_leaf"])
        if c == "V":
            npnl = max(1, round(W / 1.952))
            pw = W / npnl
            gid = f"{k}{t}{a}_{b}"
            for m in range(npnl):
                px, pz = at(jm + (m + 0.5) * pw, 0.0)
                self.put("Prop_MetalFence_Ornament", (px, y, pz), 0 if t == "h" else 90, scl=(pw / 1.952, H / 2.852, 1.0),
                         cell=cellr, kind="Opening", tags=["portcullis", "raised", "group:" + gid])
        self.wide_edges.append((k, t, a, b, c))

    def bolt_tags(self, k, t, a, b):
        bl = self.bolt_edge.get((k, t, a, b))
        return ["bolt:" + bl[1]] if bl else []

    def mark_vertex_ends(self, k, t, a, b, half):
        pass

    def extents(self, ek, beam=False):
        """Тело стен на ребре поперёк ребра (от линии ребра, по мировой оси): (низ, верх). beam — по верхней балке (±0.2)."""
        lo, hi = 9.0, -9.0
        for (f, s) in self.edge_faces.get(ek, []):
            a, b = (s - 0.2, s + 0.2) if beam else (s - WALL_BACK, s + WALL_FRONT)
            if f in ("S", "W"):
                a, b = -b, -a
            lo, hi = min(lo, a), max(hi, b)
        return (lo, hi) if lo < hi else None

    def corner_pillars(self, k):
        """Столб на стыке стен: закрывает торцы и углы (не на прямом продолжении одинаковой стены)."""
        L = self.levels[k]
        y = STOREY * k
        thick = {}
        for p in self.pieces:
            pass
        lines = defaultdict(dict)  # vertex -> {dir: half} — только стены
        bars = defaultdict(set)    # vertex -> направления решёток
        for (x0, z0, x1, z1, low), kind in zip(self.wall_lines[k], self.line_kinds[k]):
            if low:
                continue
            if kind == "bars":
                if abs((x1 - x0) - 2) < 1e-6:
                    zc = (z0 + z1) / 2
                    bars[(round(x0, 3), round(zc, 3))].add("E"); bars[(round(x1, 3), round(zc, 3))].add("W")
                else:
                    xc = (x0 + x1) / 2
                    bars[(round(xc, 3), round(z0, 3))].add("N"); bars[(round(xc, 3), round(z1, 3))].add("S")
                continue
            if abs((x1 - x0) - 2) < 1e-6:   # вдоль x
                zc = (z0 + z1) / 2; half = (z1 - z0) / 2
                lines[(round(x0, 3), round(zc, 3))]["E"] = max(half, lines[(round(x0, 3), round(zc, 3))].get("E", 0))
                lines[(round(x1, 3), round(zc, 3))]["W"] = max(half, lines[(round(x1, 3), round(zc, 3))].get("W", 0))
            else:
                xc = (x0 + x1) / 2; half = (x1 - x0) / 2
                lines[(round(xc, 3), round(z0, 3))]["N"] = max(half, lines[(round(xc, 3), round(z0, 3))].get("N", 0))
                lines[(round(xc, 3), round(z1, 3))]["S"] = max(half, lines[(round(xc, 3), round(z1, 3))].get("S", 0))
        par = defaultdict(set)
        crenel = set()
        for (x0, z0, x1, z1, low), kind in zip(self.wall_lines[k], self.line_kinds[k]):
            if kind not in ("parapet", "parapet_m"):
                continue
            if kind == "parapet_m":
                if abs((x1 - x0) - 2) < 1e-6:
                    zc = (z0 + z1) / 2
                    crenel.add((round(x0, 3), round(zc, 3))); crenel.add((round(x1, 3), round(zc, 3)))
                else:
                    xc = (x0 + x1) / 2
                    crenel.add((round(xc, 3), round(z0, 3))); crenel.add((round(xc, 3), round(z1, 3)))
            if abs((x1 - x0) - 2) < 1e-6:
                zc = (z0 + z1) / 2
                par[(round(x0, 3), round(zc, 3))].add("E"); par[(round(x1, 3), round(zc, 3))].add("W")
            else:
                xc = (x0 + x1) / 2
                par[(round(xc, 3), round(z0, 3))].add("N"); par[(round(xc, 3), round(z1, 3))].add("S")
        for (vx, vz), pd in par.items():
            if len(pd) == 2 and pd in ({"E", "W"}, {"N", "S"}):
                continue
            if len(pd) == 1 and (vx, vz) in lines:
                continue                      # конец парапета упирается в стену
            vi, vj = int(round((vx - self.OX) / CELL)), int(round((vz - self.OZ) / CELL))
            sill = PARAPET_P if (vx, vz) in crenel else 1.0
            self.put("Stairs_Exterior_SingleSideThick", (vx, y, vz), 0, scl=(1.0, sill, 0.2), cell=(vi, vj, k), kind="Wall",
                     tags=["parapet_post"])
            if (vx, vz) in crenel and not (len(pd) == 1):   # угловой зубец над столбиком
                self.put("Stairs_Exterior_SingleSideThick", (vx, y + sill, vz), 0, scl=(1.0, MERLON_H, 0.2), cell=(vi, vj, k), kind="Wall",
                         tags=["merlon"])
        # железная стойка в каждом узле решётки: связывает соседние секции и упирает решётку в стену
        for (vx, vz), bd in bars.items():
            vi, vj = int(round((vx - self.OX) / CELL)), int(round((vz - self.OZ) / CELL))
            self.put("Bars_Post", (vx, y, vz), 0, scl=(0.09, 2.72, 0.09), cell=(vi, vj, k), kind="Opening", tags=["bars_post"])
        for (vx, vz), ds in lines.items():
            keys = set(ds)
            same = len(set(ds.values())) == 1
            vi, vj = int(round((vx - self.OX) / CELL)), int(round((vz - self.OZ) / CELL))
            # торец стены, за которым по той же линии решётка: блок вровень со стеной закрывает торец.
            # Если в узле уже L-угол двух стен — его закрывает угловой блок, второй блок не нужен (не дублировать).
            is_L = len(ds) == 2 and keys not in ({"E", "W"}, {"N", "S"})
            if not is_L:
                for d in list(keys):
                    if OPP[d] not in keys and OPP[d] in bars.get((vx, vz), ()):
                        self.put("Corner_Filler", (vx, y, vz), 0, scl=(0.204, STOREY * 0.955, 0.204), cell=(vi, vj, k),
                                 kind="Wall", tags=["wall_end"])
                        break
            if len(ds) == 2 and keys in ({"E", "W"}, {"N", "S"}) and same:
                continue                      # прямое продолжение одинаковой стены
            if len(ds) >= 3:
                continue                      # Т- и Х-стык: торец примыкающей стены прячется в толще сквозной
            if len(ds) == 2 and keys not in ({"E", "W"}, {"N", "S"}):
                # L-угол: стена вдоль X продлевается на полутолщину стены вдоль Z, та укорачивается на полутолщину первой.
                # Кладка по мировым координатам сходится на углу без шва и без колонны.
                hx = "E" if "E" in keys else "W"
                vz_ = "N" if "N" in keys else "S"
                eA = (k, "h", vi if hx == "E" else vi - 1, vj)
                eB = (k, "v", vi, vj if vz_ == "N" else vj - 1)
                A, B = self.edge_pieces.get(eA, []), self.edge_pieces.get(eB, [])
                if A and B:
                    # угловой блок в узле: ровно по телу обеих стен (одинарной, двойной, наружной двойной, грани массива);
                    # стены своей длины, их торцы внутри блока; материал — как у наружной стороны угла
                    ci = vi + (-1 if hx == "E" else 0)
                    cj = vj + (-1 if vz_ == "N" else 0)
                    sx_, sz_ = (-1 if hx == "E" else 1), (-1 if vz_ == "N" else 1)   # наружу (выпуклая сторона)
                    outer = self.side_want(k, ci, cj)
                    model = "Corner_Filler_Plaster" if outer == "plaster" else "Corner_Filler"
                    zA, xB = self.extents(eA), self.extents(eB)
                    if zA and xB:
                        cx, cz = vx + (xB[0] + xB[1]) / 2, vz + (zA[0] + zA[1]) / 2
                        self.put(model, (cx, y, cz), 0, scl=(xB[1] - xB[0] + 0.004, STOREY * 0.955, zA[1] - zA[0] + 0.004),
                                 cell=(vi, vj, k), kind="Wall", tags=["corner"])
                        # наружный угол на улицу: ровный угловой камень (соединитель кладки) от лица кладки наружу
                        if self.zof(k, ci, cj) in ("OUT", "VOID", "BORDER", "WATER") and outer == "brick":
                            qs = 0.7
                            fx = xB[1] if sx_ > 0 else -xB[0]          # лицо кладки на выпуклой стороне
                            fz = zA[1] if sz_ > 0 else -zA[0]
                            qx, qz = fx - 0.025 + 0.235 * qs, fz - 0.025 + 0.235 * qs
                            self.put("Corner_Exterior_Brick", (vx + sx_ * qx, y, vz + sz_ * qz), 0, scl=(qs, WALL_SY, qs),
                                     cell=(vi, vj, k), kind="Wall", tags=["quoin"])
                    # угол верхней балки: деревянный блок по балкам обеих стен, чтобы пояс не обрывался на углу
                    bA, bB = self.extents(eA, beam=True), self.extents(eB, beam=True)
                    if bA and bB and not (eA in self.slab_edges_set and eB in self.slab_edges_set):
                        self.put("Corner_Filler_Beam", (vx + (bB[0] + bB[1]) / 2, y + STOREY * 0.955, vz + (bA[0] + bA[1]) / 2), 0,
                                 scl=(bB[1] - bB[0], STOREY * 0.045, bA[1] - bA[0]), cell=(vi, vj, k), kind="Wall", tags=["corner"])
                    continue
            # торец стены (у ворот/проёма/края) или угол с проёмом — каменный столб
            half = max(ds.values())
            s = 1.0 if half <= 0.21 else 1.85
            px, pz = vx, vz
            if len(ds) == 1:
                d = next(iter(ds))
                ek = {"E": (k, "h", vi, vj), "W": (k, "h", vi - 1, vj), "N": (k, "v", vi, vj), "S": (k, "v", vi, vj - 1)}[d]
                ext = self.extents(ek)
                if ext:
                    c0 = (ext[0] + ext[1]) / 2
                    s = max(1.0, (ext[1] - ext[0] + 0.06) / 0.47)
                    if d in ("E", "W"):
                        pz += c0
                    else:
                        px += c0
            self.put("Corner_Exterior_Brick", (px, y, pz), 0, scl=(s, WALL_SY, s),
                     cell=(vi, vj, k), kind="Wall", tags=["pillar"])

    def stretch(self, pieces, t, end, amount):
        """Удлинить (amount>0) или укоротить стену на одном конце: end 0 — у меньшей координаты, 1 — у большей."""
        for p in pieces:
            L = 2.0 * abs(p["scl"][0])
            newL = L + amount
            p["scl"][0] = (newL / 2.0) * (1 if p["scl"][0] >= 0 else -1)
            shift = (amount / 2.0) * (1 if end == 1 else -1)
            if t == "h":
                p["pos"][0] = round(p["pos"][0] + shift, 4)
            else:
                p["pos"][2] = round(p["pos"][2] + shift, 4)

    def inner_corners(self, k, vx, vz, ds):
        """Во внутреннем углу (четверть, ограниченная двумя стенами) — угловой элемент, чтобы стены не резали друг друга."""
        y = STOREY * k
        vi, vj = int(round((vx - self.OX) / CELL)), int(round((vz - self.OZ) / CELL))
        for sx in (1, -1):
            for sz in (1, -1):
                if ("E" if sx > 0 else "W") not in ds or ("N" if sz > 0 else "S") not in ds:
                    continue
                ci, cj = vi + (0 if sx > 0 else -1), vj + (0 if sz > 0 else -1)
                want = self.side_want(k, ci, cj)
                zc = self.zof(k, ci, cj)
                if want == "hidden" or zc in ("VOID", "BORDER", "OUT", "WATER"):
                    continue
                T = max(ds["E" if sx > 0 else "W"], ds["N" if sz > 0 else "S"])
                if want == "plaster":
                    # Corner_Interior_Big лежит в четверти (−x,+z): поворот в нужную четверть
                    q = {(1, 1): 0, (1, -1): 90, (-1, -1): 180, (-1, 1): 270}
                    yaw = (q[(sx, sz)] - 270) % 360
                    self.put("Corner_Interior_Big", (vx + sx * T, y, vz + sz * T), yaw, scl=(1, WALL_SY, 1),
                             cell=(ci, cj, k), kind="Wall", tags=["inner_corner"])
                else:
                    self.put("Corner_Exterior_Brick", (vx + sx * T, y, vz + sz * T), 0, scl=(0.8, WALL_SY, 0.8),
                             cell=(ci, cj, k), kind="Wall", tags=["inner_corner"])

    # ============================================================== floors, stairs, water
    def build_cells(self):
        rep = self.rep
        for k, L in self.levels.items():
            y = STOREY * k
            for (i, j), c in L.cells.items():
                x, z = self.center(i, j)
                zn = self.zof(k, i, j)
                where = f"L{k} {i},{j} '{c}'"
                below = self.ch(k - 1, i, j)
                zb = self.zof(k - 1, i, j)
                has_floor = isroom(c) or c in OUT_CH or c in STAIR_CH or (c == "H" and below != "H")
                if c == "H" and below == "H":
                    self.ladder_up(k, i, j)
                    continue
                if has_floor:
                    # что под полом
                    supported = zb == "SOLID" or below == "#" or (len(zb) == 1 if zb else False) or \
                        below in OUT_CH and False
                    if k == self.kmin:
                        supported = True
                    if (k - 1, i, j) in self.holes and self.hole_room.get((k - 1, i, j)):
                        zb = self.hole_room[(k - 1, i, j)]
                    room_below = len(zb) == 1 if zb else False
                    if c == "." or c == ",":
                        if not (zb == "SOLID" or k == self.kmin):
                            rep.e(where, f"земля висит: под ней на L{k - 1} '{below}', нужен '#'")
                        self.ground.append({"pos": [x, y, z], "mat": OUT_CH[c], "cell": [i, j, k]})
                        continue
                    if c in "=_":
                        self.put("Floor_WoodDark", (x, y, z), 0, cell=(i, j, k), kind="Floor", tags=["deck"])
                        self.put("Floor_WoodDark", (x, y - 0.22, z), 0, rx=180, cell=(i, j, k), kind="Floor", tags=["underside"])
                        # брус обвязки по краю настила: закрывает щель между досками и обшивкой снизу (иначе сбоку снизу
                        # видна изнанка досок — «вывернутые треугольники», сквозь них небо)
                        for d_, (ddx, ddz) in DIRS.items():
                            if self.ch(k, i + ddx, j + ddz) in "=_":
                                continue
                            bx_, bz_ = x + ddx * (CELL / 2 - 0.12), z + ddz * (CELL / 2 - 0.12)
                            if ddx:
                                self.put("Corner_Exterior_Wood", (bx_, y - 0.12, z - CELL / 2), 0, rx=90, scl=(1.0, CELL / 3.0, 1.0),
                                         cell=(i, j, k), kind="Trim", tags=["deck_edge"])
                            else:
                                self.put("Corner_Exterior_Wood", (x - CELL / 2, y - 0.12, bz_), 90, rx=90, scl=(1.0, CELL / 3.0, 1.0),
                                         cell=(i, j, k), kind="Trim", tags=["deck_edge"])
                        if c == "_":    # мост над водой: под настилом вода и дно, сваи до дна
                            self.put("Floor_UnevenBrick", (x, y - STOREY, z), 0, cell=(i, j, k), kind="Floor", tags=["waterbed"])
                            self.water.append({"cell": [i, j, k], "pos": [x, y - 1.0, z], "bottom": y - STOREY})
                            self.deck_posts(k, i, j, base=y - STOREY)
                        else:
                            self.deck_posts(k, i, j)
                        continue
                    on_posts = c in STAIR_CH and zn == "OUT" and self.stair_kind.get((k, i, j)) == "wood" and zb != "SOLID"
                    if on_posts:
                        self.deck_posts(k, i, j, top=STOREY * k)
                    if not (zb == "SOLID" or room_below or k == self.kmin or below in STAIR_CH or (k - 1, i, j) in self.holes
                            or below in "~L" and self.water_room.get((k - 1, i, j)) or on_posts):
                        rep.e(where, f"пол висит в воздухе: под ним на L{k - 1} '{below}' ({zb}). Нужен '#' или помещение снизу; для настила — '='")
                    rp = self.room_props(k, zn)
                    if isroom(c) or c in STAIR_CH or c == "H":
                        fl = FLOOR_MODELS.get(rp["floor"], "Floor_UnevenBrick") if rp else "Floor_UnevenBrick"
                    else:
                        fl = "Floor_UnevenBrick"
                    if c in STAIR_CH and zn == "OUT":
                        fl = None
                    if fl:
                        # мощение на земле (L0, снаружи) рисует ландшафт — край размыт в обе стороны; плитка только как запасной вариант
                        tags = ["pave"] if (c == ":" and k == 0) else None
                        self.put(fl, (x, y, z), 0, cell=(i, j, k), room=zn, kind="Floor", tags=tags)
                    if room_below or (below in "~L" and self.water_room.get((k - 1, i, j))):
                        rb = self.room_props(k - 1, zb if len(zb) == 1 else self.water_room.get((k - 1, i, j)))
                        cm = CEIL_MODELS.get(rb["ceiling"] if rb else "wood", "Floor_WoodDark")
                        self.put(cm, (x, y - 0.22, z), 0, rx=180, cell=(i, j, k), kind="Floor", tags=["ceiling"])
                    self.slab_edges(k, i, j)
                    if isroom(c):
                        self.trim_cells.append((k, i, j, c, zn))
                elif c == " ":
                    if (k, i, j) in self.holes:
                        continue
                    if len(zb or "") == 1 and not self.levels[k - 1].rooms.get(zb, {}).get("sky"):
                        if not self.roof_covers(k - 1, i, j):
                            rep.e(where, f"над комнатой '{zb}' (L{k - 1}) нет потолка/крыши: дай пол на L{k}, крышу (roof) или sky=1")
                elif c == "#":
                    # низ массива над комнатой (ворота, калитка, колонна над камерой) — потолок этой комнаты
                    zr = self.hole_room.get((k - 1, i, j)) if (k - 1, i, j) in self.holes else zb
                    if zr and len(zr) == 1:
                        rb = self.room_props(k - 1, zr)
                        cm = CEIL_MODELS.get(rb["ceiling"] if rb else "wood", "Floor_WoodDark")
                        self.put(cm, (x, y - 0.22, z), 0, rx=180, cell=(i, j, k), kind="Floor", tags=["ceiling"])
                    if k == self.kmax or self.ch(k + 1, i, j) == " ":
                        if not self.roof_covers(k, i, j):
                            self.put("Floor_UnevenBrick", (x, y + STOREY, z), 0, cell=(i, j, k + 1), kind="Floor", tags=["cap"])
                    continue
                elif c in "~L":
                    self.put("Floor_UnevenBrick", (x, y - STOREY, z), 0, cell=(i, j, k), kind="Floor", tags=["waterbed"])
                    self.water.append({"cell": [i, j, k], "pos": [x, y - 1.0, z], "bottom": y - STOREY})
                    if c == "L":
                        self.water_ladder(k, i, j)
                elif c == "O":
                    self.well(k, i, j)
                if c == "H":
                    self.ladder_up(k, i, j)
            # комнаты верхнего уровня без пола над ними
            for (i, j), c in L.cells.items():
                if (isroom(c)) and (k == self.kmax) and not L.rooms.get(c, {}).get("sky") and not self.roof_covers(k, i, j):
                    rep.e(f"L{k} {i},{j}", f"комната '{c}' на верхнем уровне без крыши (roof) и без sky=1")
        for s in self.stairs:
            self.build_stair(s)
        for (k, i, j, c, zn) in self.trim_cells:
            self.room_trims(k, i, j, c, zn)

    def grass_tufts(self, k, i, j):
        """Объёмная трава: пучки по клетке луга и гуще у края мощения/стен — без ровного обреза."""
        import random
        rnd = random.Random(i * 7919 + j * 104729 + k * 31)
        x, z = self.center(i, j)
        y = STOREY * k
        models = ("Grass2", "Grass3", "Grass2", "Grass3", "Fern2", "Flower1", "Flower4")
        pts = [(rnd.uniform(-0.85, 0.85), rnd.uniform(-0.85, 0.85)) for _ in range(int(self.site.get("grass", 3)))]
        for d, (dx, dz) in DIRS.items():
            nc = self.ch(k, i + dx, j + dz)
            e = self.edge(k, i, j, d)
            if nc in ":=,_" or nc in STAIR_CH or (e in BARRIER_CH and e not in "!"):
                for _ in range(2):    # край: пучки вдоль границы, чуть заходят на мощение
                    t = rnd.uniform(-0.8, 0.8)
                    off = rnd.uniform(0.55, 0.95)
                    pts.append((dx * off + (t if dz else 0), dz * off + (t if dx else 0)))
        for (ox, oz) in pts:
            m = rnd.choice(models[:4]) if rnd.random() < 0.8 else rnd.choice(models[4:])
            s = rnd.uniform(0.38, 0.62)
            self.put(m, (x + ox, y, z + oz), rnd.uniform(0, 360), scl=(s, s * rnd.uniform(0.8, 1.15), s), cell=(i, j, k),
                     kind="Nature", tags=["grass"])

    SKIRT_T, SKIRT_H = 0.035, 0.13

    def strip(self, model, cx, cy, cz, along, length, thick, height, cell, tag):
        """Брусок вдоль ребра: дерево (Corner_Exterior_Wood лёжа) или камень (Stairs_Exterior_SingleSideThick)."""
        if model == "Corner_Exterior_Wood":
            if along == "z":
                self.put(model, (cx, cy, cz - length / 2), 0, rx=90, scl=(thick / 0.21, length / 3.0, height / 0.24),
                         cell=cell, kind="Trim", tags=[tag])
            else:
                self.put(model, (cx - length / 2, cy, cz), 90, rx=90, scl=(thick / 0.21, length / 3.0, height / 0.24),
                         cell=cell, kind="Trim", tags=[tag])
        else:  # SingleSideThick: x — толщина, y — высота (низ в 0), z — длина
            yaw = 0 if along == "z" else 90
            self.put(model, (cx, cy - height / 2, cz), yaw, scl=(thick / 0.4, height, length / 2.0), cell=cell, kind="Trim", tags=[tag])

    def room_trims(self, k, i, j, c, zn):
        """Плинтус у пола и карниз под деревянным потолком вдоль каждой стены комнаты (правило B3)."""
        rp = self.room_props(k, zn)
        if not rp or c in STAIR_CH or c == "H":
            return
        hatch_side = c.split(":")[1] if c.startswith("hatch:") else None
        x, z = self.center(i, j)
        y = STOREY * k
        wood_floor = rp["floor"] in ("wood", "woodlight")
        has_ceiling = self.ch(k + 1, i, j) not in (" ",) or self.roof_covers(k, i, j) or \
            ((k + 1, i, j) in self.holes and self.holes[(k + 1, i, j)][0] != "tall")   # в двухсветной — карниз не на полвысоты
        for d, (dx, dz) in DIRS.items():
            t, a, b = self.edge_key(i, j, d)
            if (k, t, a, b) not in self.edge_faces:
                continue
            e = self.edge(k, i, j, d)
            face = self.wall_face_offset(k, i, j, d)
            along = "z" if dx else "x"
            spans = [(-1.0, 1.0)] if e not in "DAG" else [(-1.0, -0.8), (0.8, 1.0)]   # у двери — по бокам коробки
            if hatch_side:
                if d == hatch_side:
                    continue                                   # над проёмом люка плинтуса нет
                if d != OPP[hatch_side]:
                    hs = DIRS[hatch_side][0] + DIRS[hatch_side][1]   # проём в «+» или «-» половине вдоль этой стены
                    spans = [(-1.0, 0.0)] if hs > 0 else [(0.0, 1.0)]
            # выпуклый угол комнаты (колонна, выступ стены): брусок заходит за угол навстречу соседнему, без выреза на углу
            ux, uz = (0, 1) if dx else (1, 0)

            def convex(sign):
                ni, nj = i + ux * sign, j + uz * sign
                if self.zof(k, ni, nj) != zn:
                    return False
                t2, a2, b2 = self.edge_key(ni, nj, d)
                if (k, t2, a2, b2) in self.edge_faces:
                    return False
                return self.zof(k, ni + dx, nj + dz) == zn

            cv0, cv1 = convex(-1), convex(1)
            for s0, s1 in spans:
                def ext(T):
                    a0 = s0 - (face + T if (s0 <= -0.999 and cv0) else 0.0)
                    a1 = s1 + (face + T if (s1 >= 0.999 and cv1) else 0.0)
                    return a1 - a0, (a0 + a1) / 2
                # плинтус
                T = self.SKIRT_T if wood_floor else 0.06
                L, mid = ext(T)
                tx, tz = (0, mid) if dx else (mid, 0)
                off = CELL / 2 - face - T / 2
                px, pz = x + dx * off + tx, z + dz * off + tz
                if wood_floor:
                    self.strip("Corner_Exterior_Wood", px, y + self.SKIRT_H / 2, pz, along, L, self.SKIRT_T, self.SKIRT_H, (i, j, k), "skirting")
                else:
                    self.strip("Stairs_Exterior_SingleSideThick", px, y + 0.15 / 2, pz, along, L, 0.06, 0.15, (i, j, k), "plinth")
                # карниз под деревянным потолком
                if has_ceiling and e not in "DAG":
                    L, mid = ext(0.06)
                    tx, tz = (0, mid) if dx else (mid, 0)
                    off2 = CELL / 2 - face - 0.03
                    self.strip("Corner_Exterior_Wood", x + dx * off2 + tx, y + STOREY - 0.22 - 0.06, z + dz * off2 + tz, along, L, 0.06, 0.12,
                               (i, j, k), "cornice")

    def slab_edges(self, k, i, j):
        """Край проёма в перекрытии — балка обвязки (брус под краем пола), а не плоская доска."""
        if k == 0 and self.ch(k, i, j) in OUT_CH and self.ch(k - 1, i, j) == "#":
            return                                   # земля (ландшафт), а не перекрытие
        x, z = self.center(i, j)
        for d, (dx, dz) in DIRS.items():
            nb = (i + dx, j + dz)
            if self.ch(k, *nb) in (" ",) and self.ch(k - 1, *nb) != "#":
                h = self.holes.get((k,) + nb)
                if h and h[0] == "stair":
                    run_dir = next((s["dir"] for s in self.stairs if s["k"] == k - 1 and nb in s["run"]), None)
                    if run_dir and DIRS[run_dir][0] * dx + DIRS[run_dir][1] * dz == 0:
                        yaw = {"S": 0, "N": 180, "W": 90, "E": 270}[d]
                        self.put("HoleCover_Straight", (x, STOREY * k - 0.11, z), yaw, cell=(i, j, k), kind="Trim", tags=["slab_edge"])
                        continue
                # балка обвязки проёма: брус 0.21×0.24 под краем пола, во всю клетку
                ex, ez = x + dx * (CELL / 2 - 0.12), z + dz * (CELL / 2 - 0.12)
                if dx:   # край вдоль z
                    self.put("Corner_Exterior_Wood", (ex, STOREY * k - 0.12, z - CELL / 2), 0, rx=90, scl=(1.0, CELL / 3.0, 1.0),
                             cell=(i, j, k), kind="Trim", tags=["slab_edge"])
                else:    # край вдоль x
                    self.put("Corner_Exterior_Wood", (x - CELL / 2, STOREY * k - 0.12, ez), 90, rx=90, scl=(1.0, CELL / 3.0, 1.0),
                             cell=(i, j, k), kind="Trim", tags=["slab_edge"])

    def deck_posts(self, k, i, j, top=None, base=None):
        base_ = base
        x, z = self.center(i, j)
        kb = k - 1
        while kb >= self.kmin and self.ch(kb, i, j) in " ~L":
            kb -= 1
        # столб стоит на верхе массива (уровень выше) или на полу/земле этого уровня
        base = STOREY * (kb + 1) if self.ch(kb, i, j) == "#" else (STOREY * kb if self.ch(kb, i, j) != " " else STOREY * self.kmin)
        if self.ch(k - 1, i, j) in "~L":
            base = STOREY * (k - 1) - STOREY
        if base_ is not None:
            base = base_
        top = STOREY * k - 0.22 if top is None else top
        if top - base < 0.3:
            return
        for sx, sz in ((-0.85, -0.85), (0.85, 0.85)):
            self.put("Corner_Exterior_Wood", (x + sx, base, z + sz), 0, scl=(1.2, (top - base) / 3.0, 1.2),
                     cell=(i, j, k), kind="Wall", tags=["post"])

    def build_stair(self, s):
        k, run, d = s["k"], s["run"], s["dir"]
        y = STOREY * k
        dx, dz = DIRS[d]
        yaw_climb = {"N": 180, "S": 0, "E": 270, "W": 90}[d]  # модели поднимаются к локальному -Z
        if s["kind"] == "wood":
            x0, z0 = self.center(*run[0])
            sx, sz = x0 - dx * CELL / 2, z0 - dz * CELL / 2
            self.put("Stair_Interior_Solid", (sx, y, sz), yaw_climb, cell=(run[0][0], run[0][1], k), room=s["zone"],
                     kind="Stair", tags=["stair"])
        else:
            for n, (ci, cj) in enumerate(run):
                x, z = self.center(ci, cj)
                for m in range(n):
                    self.put("Stairs_Exterior_Platform", (x, y + m, z), yaw_climb, cell=(ci, cj, k), kind="Stair")
                self.put("Stairs_Exterior_Straight", (x, y + n, z), yaw_climb, cell=(ci, cj, k), kind="Stair", tags=["stair"])

    def ladder_up(self, k, i, j):
        rep = self.rep
        where = f"L{k} {i},{j} 'H'"
        if self.ch(k + 1, i, j) not in " H":
            rep.e(where, f"над стремянкой на L{k + 1} нужна пустота ' ' (люк) или продолжение 'H', а там '{self.ch(k + 1, i, j)}'")
        side = self.ladder_side.get((k, i, j))
        if side is None:
            rep.e(where, "стремянке не к чему прислониться: нужна стена на L%d с той стороны, где на L%d открытый выход на пол" % (k, k + 1))
            return
        x, z = self.center(i, j)
        dx, dz = DIRS[side]
        # отступ от линии стены — по телу самой толстой стены во всей шахте (двойная стена выступает на 0.29 м):
        # жерди не уходят в кладку, а капсула игрока на стремянке не трётся о стену; у люка стена тонкая — 0.28 м
        ks, kq = [k], k
        while self.ch(kq - 1, i, j) == "H":
            kq -= 1; ks.append(kq)
        kq = k
        while self.ch(kq + 1, i, j) == "H":
            kq += 1; ks.append(kq)
        depth = 0.2
        t_, a_, b_ = self.edge_key(i, j, side)
        for kk in ks:
            ext = self.extents((kk, t_, a_, b_), beam=True)   # коллайдер стены набора — по верхней балке (±0.2)
            if ext:
                depth = max(depth, ext[1] if side in ("W", "S") else -ext[0])
        off = CELL / 2 - depth - 0.08
        self.ladders.append({"pos": [x + dx * off, STOREY * k, z + dz * off], "yaw": yaw_facing(OPP[side]),
                             "height": STOREY - 0.03, "cell": [i, j, k]})
        if self.ch(k + 1, i, j) == " ":
            if self.ch(k - 1, i, j) == "H":
                self.chimney_top(k + 1, i, j, side)    # верх шахты-дымохода: каменный оголовок, без крышки
            else:
                self.hatch(k + 1, i, j, side)

    def chimney_top(self, k, i, j, side=None):
        x, z = self.center(i, j)
        y = STOREY * k
        for d in ("N", "S"):
            if d == side:
                continue
            self.put("Stairs_Exterior_SingleSideThick", (x, y, z + DIRS[d][1] * 0.8), 90, cell=(i, j, k), kind="Wall", tags=["chimney_top"])
        for d in ("E", "W"):
            if d == side:
                continue
            self.put("Stairs_Exterior_SingleSideThick", (x + DIRS[d][0] * 0.8, y, z), 0, scl=(1, 1, 0.6), cell=(i, j, k),
                     kind="Wall", tags=["chimney_top"])

    def hatch(self, k, i, j, side):
        """Люк в полу уровня k: проём 1×2 м вдоль стены стремянки (side), крышка-створка, рядом полклетки пола."""
        x, z = self.center(i, j)
        y = STOREY * k
        dx, dz = DIRS[side]
        # вторая половина клетки — дощатый пол (сверху и снизу); Half1 x 0..1, Half2 x -1..0, Half3 z -1..0
        floor_half = {"W": ("Floor_WoodDark_Half1", 0), "E": ("Floor_WoodDark_Half2", 0),
                      "S": ("Floor_WoodDark_Half3", 180), "N": ("Floor_WoodDark_Half3", 0)}[side]
        hole_half = {"W": ("Floor_WoodDark_Half2", 0), "E": ("Floor_WoodDark_Half1", 0),
                     "S": ("Floor_WoodDark_Half3", 0), "N": ("Floor_WoodDark_Half3", 180)}[side]
        self.put(floor_half[0], (x, y, z), floor_half[1], cell=(i, j, k), kind="Floor", tags=["hatch_floor"])
        room = self.hole_room.get((k, i, j))
        if room:
            self.trim_cells.append((k, i, j, "hatch:" + side, room))
        self.put(floor_half[0], (x, y - 0.22, z), floor_half[1] if side in "WE" else (floor_half[1] + 180) % 360, rx=180,
                 cell=(i, j, k), kind="Floor", tags=["ceiling"])
        # крышка: светлые доски поперёк половиц (сразу видно, что это люк), ровно на проём, чуть выше пола
        hx, hz = x + dx * 0.5, z + dz * 0.5
        along_z = side in "WE"                       # длинная сторона проёма вдоль z
        self.put("Floor_WoodLight", (hx, y + 0.03, hz), 0 if along_z else 90, scl=(0.5, 3, 1),
                 cell=(i, j, k), kind="Opening", tags=["trapdoor", "hinge:" + OPP[side], "chain"] +
                 (["bolt:" + self.bolt_hatch[(k - 1, i, j)]] if (k - 1, i, j) in self.bolt_hatch else []))
        # рамка-обвязка вокруг проёма (кроме стороны у стены): брус лёжа, верх чуть выше пола
        def strip(cx, cz, length, along):          # along: "z" или "x"
            if along == "z":
                self.put("Corner_Exterior_Wood", (cx, y - 0.02, cz - length / 2), 0, rx=90, scl=(0.6, length / 3.0, 0.5),
                         cell=(i, j, k), kind="Trim", tags=["hatch_frame"])
            else:
                self.put("Corner_Exterior_Wood", (cx - length / 2, y - 0.02, cz), 90, rx=90, scl=(0.6, length / 3.0, 0.5),
                         cell=(i, j, k), kind="Trim", tags=["hatch_frame"])
        if along_z:
            strip(x, z, 2.0, "z")                    # граница проёма и половины пола
            for e, sz_ in (("N", 1), ("S", -1)):
                if self.edge(k, i, j, e) not in WALL_CH and self.zof(k, i, j + sz_) != "SOLID":
                    strip(hx, z + sz_ * 0.94, 1.0, "x")
        else:
            strip(x, z, 2.0, "x")
            for e, sx_ in (("E", 1), ("W", -1)):
                if self.edge(k, i, j, e) not in WALL_CH and self.zof(k, i + sx_, j) != "SOLID":
                    strip(x + sx_ * 0.94, hz, 1.0, "z")

    def water_ladder(self, k, i, j):
        side = None
        for d, (dx, dz) in DIRS.items():
            if self.walkable(k, i + dx, j + dz) and self.ch(k, i + dx, j + dz) not in "~L":
                side = d
                break
        if side is None:
            self.rep.e(f"L{k} {i},{j} 'L'", "лестнице из воды некуда вывести: рядом нет проходимой клетки")
            return
        x, z = self.center(i, j)
        dx, dz = DIRS[side]
        off = CELL / 2 - 0.2 - 0.08
        self.ladders.append({"pos": [x + dx * off, STOREY * k - STOREY, z + dz * off], "yaw": yaw_facing(OPP[side]),
                             "height": STOREY + 0.05, "cell": [i, j, k], "water": True})

    def well(self, k, i, j):
        x, z = self.center(i, j)
        y = STOREY * k
        below = self.ch(k - 1, i, j)
        if below not in "~L" and not self.walkable(k - 1, i, j):
            self.rep.e(f"L{k} {i},{j} 'O'", f"колодец ведёт в '{below}' на L{k - 1}: внизу нужна вода '~'/'L' или пол")
        # колодец из деталей набора: оголовок, столбы, ворот, крыша от дождя, ведро, верёвка
        self.put("Well_Composite", (x, y, z), 0, cell=(i, j, k), kind="Prop", tags=["well"])
        for d in ("N", "S"):     # ствол: те же бортики, но ниже уровня, сквозь перекрытие цистерны
            self.put("Stairs_Exterior_SingleSideThick", (x, y - 1.0, z + DIRS[d][1] * 0.8), 90, cell=(i, j, k), kind="Wall", tags=["well_shaft"])
        for d in ("E", "W"):
            self.put("Stairs_Exterior_SingleSideThick", (x + DIRS[d][0] * 0.8, y - 1.0, z), 0, scl=(1, 1, 0.6), cell=(i, j, k),
                     kind="Wall", tags=["well_shaft"])
        self.volumes.append({"type": "well", "min": [x - 0.6, y - STOREY, z - 0.6], "max": [x + 0.6, y + 0.2, z + 0.6]})

    def roof_extent(self, r):
        """Габарит крыши в её осях с учётом len= (длина по коньку подгоняется под дом: свес над фронтоном 0.35 м от стены)."""
        mn, mx = list(CAT[r["model"]]["min"]), list(CAT[r["model"]]["max"])
        sc = r.get("s", 1.0)
        mn = [v * sc for v in mn]; mx = [v * sc for v in mx]
        if r.get("len"):
            mn[2], mx[2] = -r["len"] / 2, r["len"] / 2
        return mn, mx

    def roof_fit(self, r):
        """(масштаб по оси конька, сдвиг центра вдоль конька в осях модели) для len=."""
        if not r.get("len"):
            return 1.0, 0.0
        mn, mx = CAT[r["model"]]["min"], CAT[r["model"]]["max"]
        s = r["len"] / (mx[2] - mn[2])
        return s, -(mn[2] + mx[2]) / 2 * s

    def roof_covers(self, k, i, j):
        L = self.levels.get(k)
        if not L:
            return False
        x, z = self.center(i, j)
        for r in L.roofs:
            if r["type"] != "roof":
                continue
            mn, mx = self.roof_extent(r)
            rx, rz = self.OX + r["at"][0] * CELL, self.OZ + r["at"][1] * CELL
            corners = [rot2(a, b, r["rot"]) for a in (mn[0], mx[0]) for b in (mn[2], mx[2])]
            x0 = rx + min(c[0] for c in corners); x1 = rx + max(c[0] for c in corners)
            z0 = rz + min(c[1] for c in corners); z1 = rz + max(c[1] for c in corners)
            if x0 - 0.01 <= x - 0.9 and x + 0.9 <= x1 + 0.01 and z0 - 0.01 <= z - 0.9 and z + 0.9 <= z1 + 0.01:
                return True
        return False

    def build_roofs(self):
        for k, L in self.levels.items():
            for (i, j), c in L.cells.items():
                room = c if isroom(c) else (self.hole_room.get((k, i, j)) if (k, i, j) in self.holes else None)
                if room and self.ch(k + 1, i, j) == " " and (k + 1, i, j) not in self.holes and self.roof_covers(k, i, j):
                    x, z = self.center(i, j)
                    rp = L.rooms.get(room)
                    cm = CEIL_MODELS.get(rp["ceiling"] if rp else "wood", "Floor_WoodDark")
                    self.put(cm, (x, STOREY * (k + 1) - 0.22, z), 0, rx=180, cell=(i, j, k), kind="Floor", tags=["ceiling"])
            for r in L.roofs:
                rx, rz = self.OX + r["at"][0] * CELL, self.OZ + r["at"][1] * CELL
                s, c = self.roof_fit(r) if r["type"] == "roof" else (1.0, 0.0)
                ox, oz = rot2(0.0, c, r["rot"])
                sc = r.get("s", 1.0)
                self.put(r["model"], (rx + ox, STOREY * (k + 1) + r["y"], rz + oz), r["rot"], scl=(sc, r.get("sy", sc), round(s * sc, 4)),
                         cell=(int(r["at"][0]), int(r["at"][1]), k), kind="Roof", tags=[r["type"]])

    # ============================================================== ширина
    def widths(self):
        """A7: никаких проходов в одну клетку — каждая клетка комнаты входит в квадрат 2×2 той же комнаты
        (кроме комнат с narrow=1: лаз, шахта). Узкая улица — предупреждение."""
        for k, L in self.levels.items():
            cells = L.cells
            for (i, j), c in cells.items():
                if not isroom(c) or L.rooms.get(c, {}).get("narrow"):
                    continue
                def same(a, b):
                    ch = cells.get((a, b), " ")
                    return ch == c or self.zone.get((k, a, b)) == c or self.hole_room.get((k, a, b)) == c
                ok = any(all(same(i + a, j + b) for a in (ox, ox + 1) for b in (oz, oz + 1))
                         for ox in (-1, 0) for oz in (-1, 0))
                if not ok:
                    nm = L.rooms.get(c, {}).get("name", c)
                    self.rep.e(f"L{k} {i},{j}", f"[A7] узко: клетка комнаты '{nm}' не входит ни в один квадрат 2×2 — расширь или пометь narrow=1 (лаз)")

    # ============================================================== connectivity
    def connectivity(self):
        rep = self.rep
        sp = self.site["spawn"]
        if not sp:
            rep.e("site", "нет spawn")
            return
        if not self.walkable(*sp):
            rep.e("site", f"spawn {sp} не на проходимой клетке ('{self.ch(*sp)}')")
            return
        adj = defaultdict(set)
        run = {(s["k"],) + c for s in self.stairs for c in s["run"]}
        self.run_cells = run
        for k, L in self.levels.items():
            for (i, j), c in L.cells.items():
                if not (self.walkable(k, i, j) or c in "~LO"):
                    continue
                if c == "H" and self.zof(k, i, j) == "SHAFT":
                    continue
                if (k, i, j) in run:
                    continue                   # на марш не шагают сбоку: только вход -> марш -> выход
                for d, (dx, dz) in DIRS.items():
                    n = (i + dx, j + dz)
                    e = self.edge(k, i, j, d)
                    if (k,) + n in run:
                        continue
                    bl = self.bolt_edge.get((k,) + self.edge_key(i, j, d))
                    if bl and bl[0] != (i, j):
                        continue               # засов с той стороны: отсюда не открыть
                    if e in PASS_CH and (self.walkable(k, *n) or self.ch(k, *n) in "~L"):
                        if c in "~L" and self.ch(k, *n) not in "~L" and c != "L":
                            continue   # из воды на берег — только по лестнице L
                        adj[(k, i, j)].add((k,) + n)
                    if c == "O" and False:
                        pass
                if c == "O" and self.ch(k - 1, i, j) in "~L" + "".join(OUT_CH):
                    adj[(k, i, j)].add((k - 1, i, j))
                for d, (dx, dz) in DIRS.items():
                    if self.ch(k, i + dx, j + dz) == "O" and self.edge(k, i, j, d) in PASS_CH:
                        adj[(k, i, j)].add((k, i + dx, j + dz))
        for s in self.stairs:
            k = s["k"]
            cells = [(k,) + s["entry"]] + [(k,) + c for c in s["run"]] + [(k + 1,) + s["exit"]]
            for a, b in zip(cells, cells[1:]):
                adj[a].add(b); adj[b].add(a)
        for k, L in self.levels.items():
            for (i, j), c in L.cells.items():
                if c == "H" and self.ch(k + 1, i, j) == "H":
                    adj[(k, i, j)].add((k + 1, i, j)); adj[(k + 1, i, j)].add((k, i, j))
        for (k, i, j), (kind, side) in self.holes.items():
            if kind == "ladder" and side:
                dx, dz = DIRS[side]
                a, b = (k - 1, i, j), (k, i + dx, j + dz)
                bh = self.bolt_hatch.get((k - 1, i, j))
                if bh != "D":
                    adj[b].add(a)              # сверху вниз (засов сверху отпирается сверху)
                if bh != "U":
                    adj[a].add(b)
        self.adj = adj
        for l in self.ladders:
            if l.get("water"):
                i, j, k = l["cell"]
                for d, (dx, dz) in DIRS.items():
                    if self.walkable(k, i + dx, j + dz) and self.ch(k, i + dx, j + dz) not in "~L":
                        adj[(k, i, j)].add((k, i + dx, j + dz)); adj[(k, i + dx, j + dz)].add((k, i, j))
        seen = {sp}
        q = deque([sp])
        while q:
            u = q.popleft()
            for v in adj[u]:
                if v not in seen:
                    seen.add(v); q.append(v)
        lost = defaultdict(list)
        for k, L in self.levels.items():
            for (i, j), c in L.cells.items():
                if self.walkable(k, i, j) and (k, i, j) not in seen and self.zof(k, i, j) != "SHAFT":
                    lost[(k, self.zof(k, i, j))].append((i, j))
        for (k, z), cells in sorted(lost.items(), key=lambda t: (t[0][0], str(t[0][1]))):
            nm = self.room_props(k, z)["name"] if self.room_props(k, z) else z
            rep.e(f"L{k} '{nm}'", f"недостижимо от старта: {len(cells)} клеток, напр. {cells[:3]}")
        self.reach = seen
        self.reach_count = len(seen)

    # ============================================================== dressing
    def load_dressing(self):
        f = os.path.join(ROOT, self.loc, "dressing.txt")
        if not os.path.exists(f):
            return []
        out = []
        for n, ln in enumerate(open(f, encoding="utf-8"), 1):
            s = ln.split("#", 1)[0].strip()
            if not s:
                continue
            p = s.split()
            pid = None
            if ":" in p[0] and not p[0].startswith("on:"):
                pid, p[0] = p[0].split(":", 1)
            try:
                model = p[0]
                k = int(p[1].lstrip("Ll"))
                i, j = (int(v) for v in p[2].split(","))
                anchor = p[3] if len(p) > 3 else "c"
                kv = parse_kv(" ".join(p[4:]))
            except Exception:
                self.rep.e(f"dressing:{n}", f"не разобрать строку: {s}")
                continue
            out.append({"line": n, "id": pid, "m": model, "k": k, "i": i, "j": j, "anchor": anchor, "kv": kv})
        return out

    def wall_face_offset(self, k, i, j, d):
        """Расстояние от края клетки (ребро d) до настоящей поверхности стены со стороны этой клетки."""
        t, a, b = self.edge_key(i, j, d)
        faces = self.edge_faces.get((k, t, a, b))
        if faces:
            best = 0.0
            for face, shift in faces:
                # кладка смотрит в клетку (face == OPP[d]) -> лицо на shift+0.11; иначе тыл на 0.09-shift
                reach = (WALL_FRONT + shift) if face == OPP[d] else (WALL_BACK - shift)
                best = max(best, reach)
            return best
        e = self.edge(k, i, j, d)
        if e in "BX":
            return 0.1
        if e in "pPrf":
            return 0.2
        return 0.0

    def build_props(self):
        rep = self.rep
        items = self.load_dressing()
        placed = {}
        foot = defaultdict(list)  # k -> [(x0,z0,x1,z1,label,height)]
        clear = self.clear_zones()
        for it in items:
            m, k, i, j, an, kv = it["m"], it["k"], it["i"], it["j"], it["anchor"], it["kv"]
            where = f"dressing:{it['line']} {m}"
            if m not in CAT:
                rep.e(where, "нет такой модели в каталоге (level/catalog.json)")
                continue
            mn, mx = CAT[m]["min"], CAT[m]["max"]
            kind = CAT[m]["kind"]
            s = float(kv.get("s", 1.0))
            mn = [v * s for v in mn]; mx = [v * s for v in mx]
            x, z = self.center(i, j)
            y = STOREY * k
            yaw = float(kv.get("rot", 0))
            dxo, dzo = float(kv.get("dx", 0)), float(kv.get("dz", 0))
            wall_mount = (kind in ("WallProp", "Light") and m in WALL_MOUNT_DEFAULT_H) or m.startswith("Prop_Vine")
            hung = m == "Chandelier"
            if an.startswith("on:"):
                par = placed.get(an[3:])
                if not par:
                    rep.e(where, f"нет предмета с id '{an[3:]}' (он должен стоять выше в файле)")
                    continue
                pyaw = par["rot"][1]
                ox, oz = dxo, dzo
                px, py, pz = par["pos"]
                pmx = CAT[par["m"]]["max"]; pmn = CAT[par["m"]]["min"]
                ps = par["scl"][0]
                top = py + pmx[1] * ps
                cxp, czp = rot2((pmn[0] + pmx[0]) / 2 * ps, (pmn[2] + pmx[2]) / 2 * ps, pyaw)
                yaw = pyaw + float(kv.get("rot", 0))
                cx, cz = rot2((mn[0] + mx[0]) / 2, (mn[2] + mx[2]) / 2, yaw)
                X, Z = px + cxp + ox - cx, pz + czp + oz - cz
                Y = top - mn[1]
                # помещается ли на верх родителя
                fp = self.footprint(X, Z, yaw, mn, mx)
                pp = self.footprint(px, pz, pyaw, [v * ps for v in pmn], [v * ps for v in pmx])
                if fp[0] < pp[0] - 0.03 or fp[2] > pp[2] + 0.03 or fp[1] < pp[1] - 0.03 or fp[3] > pp[3] + 0.03:
                    rep.e(where, f"не помещается на '{an[3:]}' ({par['m']}): свисает за край")
                p = self.put(m, (X, Y, Z), yaw, scl=(s, s, s), cell=(i, j, k), kind=kind, tags=["prop"] + self.cover_tags(m, kv))
                if p and it["id"]:
                    placed[it["id"]] = p
                    p["id"] = it["id"]
                self.add_light(m, p, kv)
                continue
            if not self.walkable(k, i, j) and self.ch(k, i, j) not in "~LO":
                rep.e(where, f"клетка L{k} {i},{j} '{self.ch(k, i, j)}' не пол")
                continue
            if an == "c":
                cx, cz = rot2((mn[0] + mx[0]) / 2, (mn[2] + mx[2]) / 2, yaw)
                X, Z = x - cx + dxo, z - cz + dzo
                Y = y - mn[1] if not wall_mount else y + float(kv.get("h", 0))
                if hung:
                    Y = y + float(kv.get("h", 2.74))
            elif an in DIRS or (len(an) == 2 and an[0] in "NS" and an[1] in "EW") or an.upper() in DIRS or \
                    (len(an) == 2 and an.upper()[0] in "NS" and an.upper()[1] in "EW"):
                A = an.upper()
                back = A[0]
                yaw = yaw_facing(OPP[back]) + float(kv.get("rot", 0))
                bdx, bdz = DIRS[back]
                face = self.wall_face_offset(k, i, j, back)
                if not wall_mount and face > 0 and self.room_props(k, self.zof(k, i, j)):
                    face += self.SKIRT_T if self.room_props(k, self.zof(k, i, j))["floor"] in ("wood", "woodlight") else 0.06
                if wall_mount and face == 0.0:
                    rep.e(where, f"вешать не на что: у клетки {i},{j} с стороны {back} нет стены")
                # задняя грань предмета (локальный min z) к стене
                depth_back = -mn[2] if float(kv.get("rot", 0)) == 0 else 0
                rx, rz = rot2(0, mn[2], yaw)
                cxl, czl = rot2((mn[0] + mx[0]) / 2, 0, yaw)
                X = x + bdx * (CELL / 2 - face) - rx - cxl
                Z = z + bdz * (CELL / 2 - face) - rz - czl
                X += dxo   # смещения dx/dz — в мировых осях (восток/север), метры
                Z += dzo
                if len(A) == 2:
                    side = A[1]
                    sdx, sdz = DIRS[side]
                    sface = self.wall_face_offset(k, i, j, side)
                    if sface > 0 and self.room_props(k, self.zof(k, i, j)):
                        sface += self.SKIRT_T if self.room_props(k, self.zof(k, i, j))["floor"] in ("wood", "woodlight") else 0.06
                    fp = self.footprint(X, Z, yaw, mn, mx)
                    if sdx > 0: X += (x + CELL / 2 - sface) - fp[2]
                    if sdx < 0: X += (x - CELL / 2 + sface) - fp[0]
                    if sdz > 0: Z += (z + CELL / 2 - sface) - fp[3]
                    if sdz < 0: Z += (z - CELL / 2 + sface) - fp[1]
                if wall_mount:
                    Y = y + float(kv.get("h", WALL_MOUNT_DEFAULT_H.get(m, 1.6)))
                else:
                    Y = y - mn[1]
            else:
                rep.e(where, f"якорь '{an}' — бывает c, N/E/S/W, NE/NW/SE/SW, on:<id>")
                continue
            if wall_mount and an != "c" and not m.startswith("Prop_Vine"):
                back_e = self.edge(k, i, j, an.upper()[0])
                if back_e in "DAGWwXBCVY":
                    rep.e(where, f"[B5] настенное в проёме: за спиной ребро '{back_e}' — перенеси на глухую стену")
                if (k, i, j) in getattr(self, "run_cells", set()):
                    rep.e(where, "[B5] настенное над маршем: висит над ступенями — перенеси к ровному полу")
            p = self.put(m, (X, Y, Z), yaw, scl=(s, s, s), cell=(i, j, k), kind=kind, tags=["prop"] + self.cover_tags(m, kv))
            if not p:
                continue
            if it["id"]:
                placed[it["id"]] = p
                p["id"] = it["id"]
            self.add_light(m, p, kv)
            if wall_mount and not m.startswith("Prop_Vine") and self.ch(k + 1, i, j) != " ":
                if Y + mx[1] > STOREY * (k + 1) - 0.22 - 0.14:      # не выше карниза под потолком
                    rep.e(where, f"[A5] выше карниза: верх {Y + mx[1] - STOREY * k:.2f} м над полом, предел 2.64 м — опусти h")
            if hung:
                # люстра: под ней проходят (низ не ниже 2.2 м — голова 1.8 м и запас), цепь до самого потолка
                hole = self.holes.get((k + 1, i, j))
                ceil = STOREY * (k + 2 if hole and hole[0] == "tall" else k + 1) - 0.22
                low, top = Y + mn[1] - y, Y + mx[1]
                if low < 2.2:
                    rep.e(where, f"[A5] люстра низко: низ {low:.2f} м над полом, нужно ≥ 2.2 — в комнате 3 м люстре не место, "
                                 f"в двухсветной (tall=1) подними h")
                if top > ceil + 0.01:
                    rep.e(where, f"выше потолка: верх люстры {top - y:.2f} м над полом, потолок {ceil - y:.2f} м")
                elif ceil - top > 0.15:
                    rep.e(where, f"[A4] люстра висит в воздухе: до потолка {ceil - top:.2f} м — подними h")
            if wall_mount or hung or m.startswith("Prop_Vine"):
                continue
            fp = self.footprint(X, Z, yaw, mn, mx)
            hgt = mx[1] - mn[1]
            # высота: не выше потолка, если над клеткой есть пол
            above = self.ch(k + 1, i, j)
            if above not in " " and (Y + mx[1]) > STOREY * (k + 1) - 0.22 + 0.01:
                rep.e(where, f"выше потолка: верх {Y + mx[1] - STOREY * k:.2f} м над полом, потолок 2.78 м")
            if kind == "Nature":
                if hgt > 3.0:      # дерево: в расчёт только ствол
                    fp = (X - 0.45, Z - 0.45, X + 0.45, Z + 0.45)
                elif hgt < 1.4 and not m.startswith(("Stone", "Rock")):
                    continue       # трава, цветы, кусты — мягкие, не мешают
            # стены
            for (x0, z0, x1, z1, low) in self.wall_lines[k]:
                if self.overlap(fp, (x0, z0, x1, z1), 0.04):
                    rep.e(where, f"залезает в стену у клетки {i},{j}")
                    break
            for o in foot[k]:
                if self.overlap(fp, o[:4], 0.03) and min(hgt, o[5]) > 0.12:
                    rep.e(where, f"наезжает на '{o[4]}'")
                    break
            if hgt > 0.35:
                for cz_ in clear[k]:
                    if self.overlap(fp, cz_[:4], 0.0):
                        rep.e(where, f"перекрывает проход: {cz_[4]}")
                        break
            foot[k].append(fp + (m, hgt))
        self.prop_foot = foot
        self.prop_ids = placed

    @staticmethod
    def cover_tags(m, kv):
        """Укрытие (за ним прячутся) и заросли (в приседе видно только вплотную) — для слоя стелса."""
        t = []
        if kv.get("cover") == "1" or m.startswith(("Crate", "Barrel", "Stone", "Prop_Wagon", "Woodpile", "Well")):
            t.append("cover")
        if kv.get("hide") == "1" or m in ("Rye", "Bush1", "Bush2"):
            t.append("hide")
        return t

    def add_light(self, m, p, kv):
        if p and "glow" in kv:   # свечение любого предмета: glow=r,g,b (0..1), power=, range=
            col = [float(v) for v in kv["glow"].split(",")]
            top = CAT[m]["max"][1] * p["scl"][1]
            self.lights.append({"pos": [p["pos"][0], p["pos"][1] + top + 0.3, p["pos"][2]], "color": col,
                                "intensity": float(kv.get("power", 1.5)), "range": float(kv.get("range", 6)),
                                "shadows": kv.get("shadows", "0") == "1"})
            return
        if not p or m not in LIGHTS or kv.get("light") == "none":
            return
        col, inten, rng, off = LIGHTS[m]
        s = p["scl"][0]
        ox, oz = rot2(off[0] * s, off[2] * s, p["rot"][1])
        self.lights.append({"pos": [p["pos"][0] + ox, p["pos"][1] + off[1] * s, p["pos"][2] + oz], "color": list(col),
                            "intensity": float(kv.get("power", inten)), "range": float(kv.get("range", rng)),
                            "shadows": kv.get("shadows", "0") == "1"})

    def footprint(self, X, Z, yaw, mn, mx):
        cs = [rot2(a, b, yaw) for a in (mn[0], mx[0]) for b in (mn[2], mx[2])]
        return (X + min(c[0] for c in cs), Z + min(c[1] for c in cs), X + max(c[0] for c in cs), Z + max(c[1] for c in cs))

    @staticmethod
    def overlap(a, b, eps):
        return a[0] < b[2] - eps and b[0] < a[2] - eps and a[1] < b[3] - eps and b[1] < a[3] - eps

    def clear_zones(self):
        """Свободные коридоры 1.2 м перед дверями/воротами/выходами лестниц/стремянками."""
        zones = defaultdict(list)
        for (k, t, a, b) in self.door_edges:
            x, z = self.edge_center(t, a, b)
            if t == "h":
                zones[k].append((x - 0.6, z - 1.3, x + 0.6, z + 1.3, f"дверь {t}{a},{b}"))
            else:
                zones[k].append((x - 1.3, z - 0.6, x + 1.3, z + 0.6, f"дверь {t}{a},{b}"))
        for s in self.stairs:
            for (k, c, nm) in ((s["k"], s["entry"], "вход на лестницу"), (s["k"] + 1, s["exit"], "выход с лестницы")):
                x, z = self.center(*c)
                zones[k].append((x - 0.7, z - 0.7, x + 0.7, z + 0.7, nm))
            for c in s["run"]:
                x, z = self.center(*c)
                zones[s["k"]].append((x - 0.9, z - 0.9, x + 0.9, z + 0.9, "лестница"))
        for l in self.ladders:
            x, y, z = l["pos"]
            zones[l["cell"][2]].append((x - 0.6, z - 0.6, x + 0.6, z + 0.6, "стремянка"))
        return zones

    # ============================================================== стража и навигация
    def guard_graph(self):
        """Граф для стражи: пол, лестницы, двери D и арки A. Без воды, стремянок, люков, прыжков, решёток,
        трещин, ворот G, засовов и комнат nav=player (лаз, сток)."""
        g = defaultdict(set)
        run = getattr(self, "run_cells", set())
        def ok(k, i, j):
            c = self.ch(k, i, j)
            if not self.walkable(k, i, j) or c in "~LOH" or self.zof(k, i, j) == "SHAFT":
                return False
            rp = self.room_props(k, self.zof(k, i, j))
            return not (rp and rp.get("nav") == "player")
        for k, L in self.levels.items():
            for (i, j) in L.cells:
                if not ok(k, i, j) or (k, i, j) in run:
                    continue
                for d, (dx, dz) in DIRS.items():
                    n = (i + dx, j + dz)
                    if (k,) + n in run or not ok(k, *n):
                        continue
                    e = self.edge(k, i, j, d)
                    if e not in " DAY" or (k,) + self.edge_key(i, j, d) in self.bolt_edge:
                        continue
                    g[(k, i, j)].add((k,) + n)
        for s in self.stairs:
            k = s["k"]
            cells = [(k,) + s["entry"]] + [(k,) + c for c in s["run"]] + [(k + 1,) + s["exit"]]
            if not (ok(*cells[0]) and ok(*cells[-1])):
                continue
            for a, b in zip(cells, cells[1:]):
                g[a].add(b); g[b].add(a)
        return g

    @staticmethod
    def bfs_path(g, a, b):
        prev = {a: None}
        q = deque([a])
        while q:
            u = q.popleft()
            if u == b:
                break
            for v in g[u]:
                if v not in prev:
                    prev[v] = u; q.append(v)
        if b not in prev:
            return None
        out = [b]
        while prev[out[-1]] is not None:
            out.append(prev[out[-1]])
        return out[::-1]

    def load_guards(self):
        f = os.path.join(ROOT, self.loc, "guards.txt")
        self.guards = []
        if not os.path.exists(f):
            return
        rep = self.rep
        g = self.guard_graph()
        ids = getattr(self, "prop_ids", {})
        G = {}
        order = []
        def cellpos(k, i, j):
            x, z = self.center(i, j)
            return [round(x, 3), STOREY * k, round(z, 3)]
        for n, ln in enumerate(open(f, encoding="utf-8"), 1):
            s = ln.split("#", 1)[0].strip()
            if not s:
                continue
            p = s.split()
            where = f"guards:{n}"
            try:
                if p[0] == "guard":
                    k = int(p[3].lstrip("Ll")); i, j = (int(v) for v in p[4].split(","))
                    kv = parse_kv(" ".join(p[5:]) + " " + p[2])
                    G[p[1]] = {"id": p[1], "role": kv.get("role", "patrol"), "k": k, "cell": [i, j], "pos": cellpos(k, i, j),
                               "face": kv.get("face", "N"), "walk": float(kv.get("walk", 1.5)), "run": float(kv.get("run", 5.8)),
                               "eye": kv.get("eye", "15/5"), "ear": float(kv.get("ear", 12)),
                               "mask": kv.get("mask", "walk,door,stair").split(","), "route": "loop", "wps": [], "sleep": None}
                    order.append(p[1])
                    if not self.walkable(k, i, j):
                        rep.e(where, f"[G5] страж {p[1]} стоит не на полу L{k} {i},{j}")
                elif p[0] == "route":
                    G[p[1]]["route"] = p[2]
                elif p[0] == "wp":
                    k = int(p[2].lstrip("Ll")); i, j = (int(v) for v in p[3].split(","))
                    kv = parse_kv(" ".join(p[4:]))
                    wp = {"k": k, "cell": [i, j], "pos": cellpos(k, i, j), "wait": float(kv.get("wait", 0)),
                          "look": kv.get("look", ""), "every": int(kv.get("every", 1)), "line": n}
                    for key in ("sit", "do"):
                        if key in kv:
                            pid = kv[key].split(":")[-1]
                            if pid not in ids:
                                rep.e(where, f"[G5] {key}={kv[key]}: нет предмета с id '{pid}' в dressing")
                            else:
                                wp[key] = kv[key]
                                wp[key + "_pos"] = ids[pid]["pos"]
                    G[p[1]]["wps"].append(wp)
                elif p[0] == "sleep":
                    kv = parse_kv(" ".join(p[2:]))
                    bed = kv.get("bed")
                    if bed not in ids:
                        rep.e(where, f"[G5] кровать '{bed}' не найдена в dressing")
                    else:
                        G[p[1]]["sleep"] = {"bed": bed, "pos": ids[bed]["pos"], "rot": ids[bed]["rot"][1],
                                            "wake": float(kv.get("wake", 12)), "on_wake": kv.get("on_wake", "get_up")}
                else:
                    rep.e(where, f"неизвестная строка: {p[0]}")
            except (KeyError, IndexError, ValueError) as ex_:
                rep.e(where, f"не разобрать: {s} ({ex_})")
        for gid in order:
            gd = G[gid]
            start = (gd["k"],) + tuple(gd["cell"])
            pts = [start] + [(w["k"],) + tuple(w["cell"]) for w in gd["wps"]]
            for w in gd["wps"]:
                c = (w["k"],) + tuple(w["cell"])
                if c not in g and len(pts) > 1:
                    rep.e(f"guards:{w['line']}", f"[G5] точка {gid} L{c[0]} {c[1]},{c[2]} '{self.ch(*c)}' не для стражи "
                                                 f"(вода, стремянка, марш, лаз или стена)")
            total = 0
            seq = list(zip(pts, pts[1:]))
            if gd["route"] == "loop" and len(pts) > 2:
                seq.append((pts[-1], pts[1]))
            for a_, b_ in seq:
                path = self.bfs_path(g, a_, b_)
                if path is None:
                    rep.e(f"guards {gid}", f"[G5] нет пути стражи L{a_[0]} {a_[1]},{a_[2]} -> L{b_[0]} {b_[1]},{b_[2]}")
                    continue
                total += (len(path) - 1) * CELL
            if gd["route"] == "back":
                total *= 2
            waits = sum(w["wait"] for w in gd["wps"])
            gd["length"] = total
            per = total / max(gd["walk"], 0.1) + waits
            self.rep.i(f"страж {gid} ({gd['role']}): маршрут {gd['route']} {total:.0f} м, цикл ≈{per:.0f} с")
            self.guards.append(gd)
        # обход стражи для ходов игрока: насколько игрок отрывается
        self.guard_g = g

    def nav_block(self):
        doors = []
        for k, L in self.levels.items():
            y = STOREY * k
            for (t, a, b) in self.all_edges(k):
                c = (L.h if t == "h" else L.v).get((a, b), " ")
                if c not in "DAGXC":
                    continue
                if c == "X" and (k, t, a, b) not in self.bolt_edge and False:
                    pass
                x, z = self.edge_center(t, a, b)
                bl = self.bolt_edge.get((k, t, a, b))
                doors.append({"kind": c, "k": k, "pos": [round(x, 3), y, round(z, 3)], "yaw": 0 if t == "h" else 90,
                              "bolt": bl[1] if bl else None})
        for (k, i, j), side in self.bolt_hatch.items():
            x, z = self.center(i, j)
            doors.append({"kind": "H", "k": k + 1, "pos": [round(x, 3), STOREY * (k + 1), round(z, 3)], "yaw": 0, "bolt": side})
        areas = []
        for k, L in self.levels.items():
            for (i, j), c in L.cells.items():
                rp = self.room_props(k, self.zof(k, i, j))
                if not rp or not (rp.get("narrow") or rp.get("nav")):
                    continue
                x, z = self.center(i, j)
                areas.append({"area": "PlayerOnly" if rp.get("nav") == "player" else "Narrow", "k": k,
                              "pos": [round(x, 3), STOREY * k, round(z, 3)]})
        exits = []
        for (n, k, i, j, d) in self.site.get("exits", []):
            t, a, b = self.edge_key(i, j, d)
            e = self.edge(k, i, j, d)
            if e not in PASS_CH:
                self.rep.e(f"site exit {n}", f"ребро выхода L{k} {i},{j} {d} '{e}' не проходимо")
            elif (k, i, j) not in getattr(self, "reach", set()):
                self.rep.e(f"site exit {n}", f"выход L{k} {i},{j} {d} недостижим от старта")
            x, z = self.edge_center(t, a, b)
            exits.append({"name": n, "k": k, "pos": [round(x, 3), STOREY * k, round(z, 3)], "yaw": 0 if t == "h" else 90})
        return {"doors": doors, "areas": areas, "exits": exits, "finish": self.site.get("finish")}

    # ============================================================== run
    def compile(self):
        self.door_edges = []
        self.wide_edges = []
        self.infer_zones()
        self.check_and_build_edges()
        self.build_cells()
        self.build_roofs()
        self.widths()
        self.connectivity()
        self.build_props()
        self.load_guards()
        self.nav = self.nav_block()
        return self

    def write(self):
        sp = self.site["spawn"]
        sx, sz = self.center(sp[1], sp[2]) if sp else (0, 0)
        rooms = {}
        for k, L in self.levels.items():
            for r, p in L.rooms.items():
                rooms[f"L{k}:{r}"] = p["name"]
        out = {"name": self.site["name"], "cell": CELL, "storey": STOREY, "origin": [self.OX, self.OZ],
               "size": [self.W, self.H], "levels": [self.kmin, self.kmax],
               "spawn": [sx, STOREY * sp[0] if sp else 0, sz], "rooms": rooms,
               "pieces": self.pieces, "ground": self.ground, "water": self.water, "ladders": self.ladders,
               "lights": self.lights, "volumes": self.volumes,
               "points": [{"name": n, "pos": [self.center(i, j)[0], STOREY * k, self.center(i, j)[1]]}
                          for (n, k, i, j) in self.site.get("points", [])],
               "guards": getattr(self, "guards", []), "nav": getattr(self, "nav", {}),
               "errors": len(self.rep.err), "warnings": len(self.rep.warn),
               # уровень земли L0 для ландшафта: строки j = 0..H-1, символы клеток
               "l0": ["".join(self.ch(0, i, j) for i in range(self.W)) for j in range(self.H)] if 0 in self.levels else []}
        path = os.path.join(ROOT, self.loc, "build.json")
        json.dump(out, open(path, "w", encoding="utf-8"), ensure_ascii=False)
        return path


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    loc = sys.argv[1] if len(sys.argv) > 1 else "fortress"
    s = Site(loc).compile()
    path = s.write()
    rep = s.rep
    kinds = defaultdict(int)
    for p in s.pieces:
        kinds[p["kind"]] += 1
    print(f"{loc}: уровни L{s.kmin}..L{s.kmax}, блоков {len(s.pieces)} ({dict(kinds)}), земля {len(s.ground)}, "
          f"вода {len(s.water)}, стремянок {len(s.ladders)}, лестниц {len(s.stairs)}, огней {len(s.lights)}, "
          f"достижимо клеток {getattr(s, 'reach_count', 0)}")
    for m in rep.err[:200]:
        print(m)
    if len(rep.err) > 200:
        print(f"... и ещё {len(rep.err) - 200} ошибок")
    for m in rep.warn[:60]:
        print(m)
    for m in rep.info:
        if m.startswith("страж"):
            print(m)
    print(f"ошибок {len(rep.err)}, предупреждений {len(rep.warn)} -> {path}")
    if "--strict" in sys.argv and rep.err:
        sys.exit(1)


if __name__ == "__main__":
    main()
