"""Генератор карт крепости-тюрьмы (level/fortress) для компилятора gridc.

Карта задаётся прямоугольниками в «координатах замысла» (клетки i 0..50, j 0..40), потом обрезается до рва.
Стены ставятся сами по правилам ниже, проёмы (двери, окна, решётки, ворота) — явным списком.

    python -I level/fortress/_gen.py        # пишет site.txt и L-1..L4.txt рядом

Правила стен (если ребро не задано явно):
- одна и та же область -> открыто;
- массив '#' или край карты -> открыто (грань массива облицовывается сама);
- комната против чего угодно другого -> стена;
- улица/вода между собой -> открыто;
- улица против пустоты -> парапет 'p';
- проём лестницы/люка: к своей комнате — перила 'r' (к улице — парапет), к выходу — открыто, к чужому — стена.
"""
import os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
CROP = (1, 1, 49, 39)                 # i0, j0, i1, j1 включительно: внешний берег по краю карты
LEVELS = range(-1, 6)
OUTC = set(".:,=_")                     # _ — настил моста над водой
SDIR = {"^": (0, 1), "v": (0, -1), ">": (1, 0), "<": (-1, 0)}
SIDE = {"N": (0, 1), "E": (1, 0), "S": (0, -1), "W": (-1, 0)}

cell = {k: {} for k in LEVELS}
reg = {k: {} for k in LEVELS}
edge = {k: {} for k in LEVELS}
rooms = {k: [] for k in LEVELS}
roofs = {k: [] for k in LEVELS}
bolts = {k: [] for k in LEVELS}
TOPS = set()


def inside(i, j):
    return CROP[0] <= i <= CROP[2] and CROP[1] <= j <= CROP[3]


def put(k, i, j, ch, r=None):
    if r is None:
        if ch.islower() and ch != "v": r = ch
        elif ch in OUTC: r = "OUT"
        elif ch == "#": r = "SOLID"
        elif ch == " ": r = "VOID"
        elif ch in "~L": r = "WATER"
        else: raise ValueError(f"нужна область для '{ch}' L{k} {i},{j}")
    cell[k][(i, j)] = ch
    reg[k][(i, j)] = r


def rect(k, i0, i1, j0, j1, ch, r=None, only=None, skip=None):
    for i in range(i0, i1 + 1):
        for j in range(j0, j1 + 1):
            cur = cell[k].get((i, j))
            if only is not None and cur not in only: continue
            if skip is not None and cur in skip: continue
            put(k, i, j, ch, r)


def R(k, c):
    if not inside(*c):
        return "BORDER"
    return reg[k].get(c, "SOLID" if k == -1 else "VOID")


def is_room(r):
    return isinstance(r, str) and len(r) == 1


def stair(k, i, j, ch, n):
    """Марш из n клеток от (i,j) в сторону стрелки. Вход — клетка перед маршем, выход — после, на уровне выше."""
    dx, dz = SDIR[ch]
    entry = (i - dx, j - dz)
    cells = [(i + t * dx, j + t * dz) for t in range(n)]
    ex = (i + n * dx, j + n * dz)
    own = R(k, entry)
    hown = R(k + 1, ex)
    for c in cells:
        put(k, *c, ch, own)
    TOPS.add((k, cells[-1], ex))                     # верх марша: ребро к клетке под выходом не ограждается
    hole = ("HOLE", hown, ex, frozenset(cells), "stair")
    for c in cells:
        put(k + 1, *c, " ", hole)


def hatch(k, i, j, side, owner):
    """Стремянка H на уровне k у стены side; на k+1 — люк с выходом на соседнюю клетку за этой стеной."""
    put(k, i, j, "H", owner)
    dx, dz = SIDE[side]
    ex = (i + dx, j + dz)
    put(k + 1, i, j, " ", ("HOLE", R(k + 1, ex), ex, frozenset([(i, j)]), "ladder"))


def E(k, i, j, d, ch):
    key = {"N": ("h", i, j + 1), "S": ("h", i, j), "E": ("v", i + 1, j), "W": ("v", i, j)}[d]
    edge[k][key] = ch


def auto(k, P, Q, horiz):
    rp, rq = R(k, P), R(k, Q)
    wall = "-" if horiz else "|"
    if (k, P, Q) in TOPS or (k, Q, P) in TOPS:
        return " "
    if rp == rq:
        return " "
    if "SOLID" in (rp, rq) or "BORDER" in (rp, rq):
        return " "
    hp, hq = isinstance(rp, tuple), isinstance(rq, tuple)
    if hp or hq:
        if hp and hq:
            return " " if rp[1] == rq[1] else wall
        h, other, oc = (rp, rq, Q) if hp else (rq, rp, P)
        own, ex, kind = h[1], h[2], h[4]
        if oc == ex:
            return " "
        if other == own:
            if kind == "ladder":
                return " "                     # люк: крышка вровень с полом, без ограждения (D2)
            return "r" if is_room(own) else "p"
        if is_room(own):
            return wall
        if other == "VOID":
            # над стеной уровня ниже парапет продолжается (край крыши), над открытым боком марша — нет
            t, a, b = ("h", P[0], Q[1]) if horiz else ("v", Q[0], P[1])
            return "p" if k - 1 in LEVELS and edge_char(k - 1, t, a, b) in "|-DAWwG" else " "
        if other == "WATER":
            return " "
        if is_room(other) or other == "SHAFT":
            return wall
        return "p"
    if is_room(rp) or is_room(rq) or "SHAFT" in (rp, rq):
        return wall
    if {rp, rq} == {"OUT", "VOID"}:
        return "p"
    return " "


def edge_char(k, t, a, b):
    if (t, a, b) in edge[k]:
        return edge[k][(t, a, b)]
    if t == "h":
        P, Q = (a, b - 1), (a, b)
    else:
        P, Q = (a - 1, b), (a, b)
    return auto(k, P, Q, t == "h")


def default_cell(k, i, j):
    return "#" if k == -1 else " "


def emit(k):
    i0, j0, i1, j1 = CROP
    W, H = i1 - i0 + 1, j1 - j0 + 1
    out = []
    for r in range(2 * H + 1):
        row = []
        for c in range(2 * W + 1):
            if r % 2 == 1 and c % 2 == 1:
                i, j = i0 + (c - 1) // 2, j0 + H - 1 - (r - 1) // 2
                row.append(cell[k].get((i, j), default_cell(k, i, j)))
            elif r % 2 == 0 and c % 2 == 1:
                row.append(edge_char(k, "h", i0 + (c - 1) // 2, j0 + H - r // 2))
            elif r % 2 == 1 and c % 2 == 0:
                row.append(edge_char(k, "v", i0 + c // 2, j0 + H - 1 - (r - 1) // 2))
            else:
                row.append(" ")
        out.append("".join(row).rstrip())
    return out


def room(k, ch, name, floor, walls, ceiling, **kw):
    extra = "".join(f" {a}={b}" for a, b in kw.items())
    rooms[k].append(f'room {ch} name="{name}" floor={floor} walls={walls} ceiling={ceiling}{extra}')


def bolt(k, i, j, d, side):
    """Засов на ребре d клетки (i,j) со стороны side (N/E/S/W); d='U' — люк стремянки, side='U' — засов сверху."""
    bolts[k].append(f"bolt {i - CROP[0]},{j - CROP[1]} {d} side={side}")


def roof(k, kind, model, at, rot, extra=""):
    rx, rz = at[0] - CROP[0], at[1] - CROP[1]
    roofs[k].append(f"{kind} {model} at={rx:g},{rz:g} rot={rot:g}{extra}")


# =========================================================================================== план
def ring(k, ch, i0, i1, j0, j1, w=2, r=None):
    """Кольцо шириной w по краю прямоугольника."""
    for i in range(i0, i1 + 1):
        for j in range(j0, j1 + 1):
            if i < i0 + w or i > i1 - w or j < j0 + w or j > j1 - w:
                put(k, i, j, ch, r)


# ---- L0: внешний берег, ров, бермы, куртина, башни за линией стены, двор
rect(0, 1, 49, 1, 39, ".")                           # внешний берег (за ним луг, граница мира, круг)
rect(0, 2, 48, 2, 38, "~")                           # ров: З/В 4 клетки, Ю/С 3 клетки
rect(0, 6, 44, 5, 35, ".")                           # остров: бермы j5/j35, куртина, двор
ring(0, "#", 6, 44, 6, 34)                           # куртина 4 м
rect(0, 25, 27, 35, 35, ":")                         # пристань
rect(0, 4, 7, 4, 7, "#")                             # ЮЗ башня, вынесена на 2 клетки
rect(0, 43, 46, 4, 7, "#")                           # ЮВ башня
rect(0, 43, 46, 33, 36, "#")                         # СВ башня (срезана до хода)
rect(0, 23, 27, 6, 9, "#")                           # надвратная
rect(0, 24, 26, 6, 9, "g")
rect(0, 26, 26, 33, 34, "w")
put(0, 26, 36, "L"); put(0, 30, 4, "L"); put(0, 42, 36, "L")      # из рва внутрь: пристань, южная и северная бермы
put(0, 14, 2, "L"); put(0, 48, 12, "L"); put(0, 32, 38, "L")      # из рва наружу: внешний берег (побег)
rect(0, 6, 7, 23, 26, "n")                           # ход в галерею в теле западной куртины
put(0, 20, 6, "x")                                   # устье стока в теле южной куртины
rect(0, 24, 26, 2, 4, "_")                           # мост через ров к воротам: 6 м, подводы разъезжаются
rect(0, 24, 26, 5, 5, ":"); rect(0, 24, 26, 1, 1, ":")   # мощёный подъезд на берме и дорога на внешнем берегу
# донжон — отдельно стоящая башня 6x6 (12x12 м), проулки вокруг
rect(0, 11, 16, 25, 30, "h")
rect(0, 17, 22, 29, 32, "k")
rect(0, 23, 28, 29, 32, "s")
for (a, b, c, d) in ((18, 26, 14, 22), (17, 23, 23, 28), (10, 17, 15, 23), (24, 26, 10, 13), (27, 29, 8, 10),
                     (9, 16, 9, 15), (24, 28, 23, 28), (27, 42, 12, 13), (40, 42, 12, 14), (30, 31, 14, 26),
                     (8, 10, 22, 32), (11, 16, 31, 32), (11, 16, 23, 24),     # проулки донжона
                     (20, 20, 8, 12), (19, 25, 13, 13)):                       # плиты над стоком
    rect(0, a, b, c, d, ":", only={"."})
rect(0, 32, 39, 22, 30, ",", only={".", ":"})
rect(0, 10, 14, 10, 14, " ")
put(0, 15, 10, " ")
put(0, 22, 18, "O", "OUT")
rect(0, 32, 36, 8, 8, "#")

# ---- L-1
room(-1, "a", "Камера №1", "stone", "stone", "stone")
room(-1, "b", "Камера", "stone", "stone", "stone")
room(-1, "d", "Камера", "stone", "stone", "stone")
room(-1, "e", "Камера №2", "stone", "stone", "stone")
room(-1, "c", "Тюремный погреб", "stone", "stone", "stone")
room(-1, "l", "Лазарет", "wood", "plaster", "wood")
room(-1, "y", "Ход к цистерне", "stone", "stone", "stone")
room(-1, "z", "Цистерна", "stone", "stone", "stone")
room(-1, "t", "Туннель", "stone", "stone", "stone", narrow=1, nav="player")
room(-1, "x", "Сток", "stone", "stone", "stone", narrow=1, nav="player")
rect(-1, 9, 16, 27, 28, "c"); rect(-1, 15, 16, 29, 31, "c")
rect(-1, 9, 10, 29, 30, "a"); rect(-1, 12, 13, 29, 30, "b")
rect(-1, 9, 10, 25, 26, "d"); rect(-1, 12, 13, 25, 26, "e")
rect(-1, 17, 22, 26, 31, "l")
rect(-1, 21, 22, 22, 25, "y")
rect(-1, 19, 25, 13, 21, "z")
rect(-1, 21, 23, 17, 19, "~", "z")
put(-1, 22, 17, "L", "z")
rect(-1, 12, 12, 15, 24, "t")
rect(-1, 20, 20, 8, 12, "x")
rect(-1, 10, 14, 10, 14, ":"); put(-1, 15, 10, ":")
E(-1, 9, 29, "S", "X"); E(-1, 10, 29, "S", "B")
E(-1, 12, 29, "S", "D"); E(-1, 13, 29, "S", "B")
E(-1, 9, 26, "N", "B"); E(-1, 10, 26, "N", "D")
E(-1, 12, 26, "N", "B"); E(-1, 13, 26, "N", "D")
E(-1, 12, 25, "S", "A")                               # пролом из туннеля в камеру №2: прокопан узниками (не тупик)
E(-1, 12, 15, "S", "A")
E(-1, 16, 27, "E", "D")
E(-1, 21, 25, "N", "D"); bolt(-1, 21, 25, "N", "S")   # лазарет <-> ход: засов со стороны хода (цистерна -> лазарет только вниз)
E(-1, 21, 22, "S", "A")
E(-1, 20, 13, "S", "X"); bolt(-1, 20, 13, "S", "N")   # решётка стока с засовом изнутри: отпер — вход из рва открыт навсегда

# ---- L0 комнаты и проёмы
room(0, "h", "Зал", "brick", "stone", "wood", plain=1, tall=1)   # двухсветный: L0+L1, галерея на L1
room(0, "k", "Кухня", "redbrick", "plaster", "wood")
room(0, "s", "Склад", "wood", "stone", "wood")
room(0, "g", "Воротный проход", "stone", "stone", "stone", plain=1)
room(0, "w", "Калитка", "stone", "stone", "stone", narrow=1, plain=1)
room(0, "n", "Ход в галерею", "stone", "stone", "stone", plain=1)
room(0, "x", "Устье стока", "stone", "stone", "stone", narrow=1, plain=1, nav="player")
for _i in (24, 25, 26):
    E(0, _i, 6, "S", "V"); E(0, _i, 9, "N", "Y")        # ворота 4 м с решёткой и створками; проезд во двор 4 м
for _j in (2, 3, 4):
    E(0, 24, _j, "W", "r"); E(0, 26, _j, "E", "r")      # перила моста
E(0, 13, 25, "S", "A"); E(0, 14, 25, "S", "A")       # зал: две арки во двор
E(0, 12, 25, "S", "w"); E(0, 15, 25, "S", "w")
E(0, 11, 26, "W", "D")                               # зал -> западный проулок
E(0, 11, 30, "W", "w"); E(0, 13, 30, "N", "w"); E(0, 15, 30, "N", "w"); E(0, 16, 25, "E", "w")
E(0, 16, 29, "E", "D")                               # зал -> кухня
E(0, 17, 31, "W", "D")                               # кухня -> северный проулок
E(0, 19, 29, "S", "D"); E(0, 17, 29, "S", "W"); E(0, 21, 29, "S", "W")
E(0, 22, 31, "E", "D"); bolt(0, 22, 31, "E", "E")     # кухня <-> склад: засов со стороны склада (петля C)
E(0, 25, 29, "S", "Y"); E(0, 26, 29, "S", "Y")        # склад: проезд 3.7 м — подвода заезжает под крышу
E(0, 24, 29, "S", "W"); E(0, 27, 29, "S", "W"); E(0, 28, 31, "E", "W")
E(0, 26, 32, "N", "A"); E(0, 26, 34, "N", "D")
E(0, 7, 26, "E", "D")                                # проулок -> ход в галерею
E(0, 20, 6, "S", "A")                                # устье стока -> южная берма
for i in range(32, 40):
    E(0, i, 22, "S", "f"); E(0, i, 30, "N", "f")
for j in range(22, 31):
    E(0, 39, j, "E", "f")
    if j != 26:
        E(0, 32, j, "W", "f")

# ---- L1
room(1, "h", "Зал, галерея", "wood", "stone", "wood", plain=1)
room(1, "g", "Стенная галерея", "stone", "stone", "stone", plain=1)
room(1, "t", "Башня лучника, нижний ярус", "wood", "stone", "wood", plain=1)
ring(1, "#", 6, 44, 6, 34)
rect(1, 43, 46, 4, 7, "#"); rect(1, 43, 46, 33, 36, "#"); rect(1, 23, 27, 6, 9, "#")
rect(1, 4, 7, 4, 7, "t")
rect(1, 6, 7, 8, 29, "g")
rect(1, 11, 16, 25, 29, " ", ("HOLE", "h", None, frozenset(), "tall"))   # второй свет зала
rect(1, 11, 16, 30, 30, "h"); put(1, 16, 28, "h"); put(1, 16, 29, "h")      # галерея: северная и восточная
rect(1, 17, 28, 29, 32, ":")
rect(1, 8, 10, 29, 30, "=", "OUT")                   # мост над западным проулком
put(1, 32, 8, ":"); put(1, 36, 8, "#")
put(1, 42, 17, "=", "OUT")
E(1, 11, 30, "W", "D"); E(1, 7, 29, "E", "D")
for i in (8, 9, 10):
    E(1, i, 29, "S", "r"); E(1, i, 30, "N", "r")
E(1, 16, 29, "E", "D")
E(1, 6, 7, "N", "D")
for j in (10, 13, 16, 19):
    E(1, 7, j, "E", "w")
for j in (12, 18, 25):
    E(1, 6, j, "W", "w")
E(1, 4, 7, "N", "w"); E(1, 5, 7, "N", "w"); E(1, 7, 4, "E", "w"); E(1, 7, 5, "E", "w")
E(1, 12, 25, "S", "w"); E(1, 14, 25, "S", "w"); E(1, 11, 26, "W", "w"); E(1, 15, 30, "N", "w"); E(1, 16, 25, "E", "w")
E(1, 42, 17, "W", "r")

# ---- L2
room(2, "b", "Казарма", "wood", "plaster", "wood", plain=1)
room(2, "t", "Башня лучника", "wood", "stone", "wood", plain=1)
room(2, "s", "Башня ЮВ", "wood", "stone", "wood", plain=1)
room(2, "u", "Надвратная", "wood", "stone", "wood", plain=1)
ring(2, ":", 6, 44, 6, 34)
put(2, 36, 8, ":"); put(2, 42, 20, "=", "OUT")
rect(2, 11, 16, 25, 30, "b")
rect(2, 4, 7, 4, 7, "t")
rect(2, 43, 46, 4, 7, "s")
rect(2, 43, 46, 33, 36, ":")
rect(2, 23, 27, 6, 9, "u")
roof(2, "roof", "Roof_Tower_RoundTiles", (45, 6), 0, " s=1.65 sy=1.05")
E(2, 6, 7, "N", "D"); E(2, 7, 6, "E", "D")
E(2, 4, 7, "W", "w"); E(2, 5, 7, "N", "w"); E(2, 7, 4, "E", "w")
E(2, 43, 6, "W", "D"); E(2, 44, 7, "N", "D")
E(2, 45, 4, "S", "w"); E(2, 46, 5, "E", "w"); E(2, 43, 4, "W", "w")
E(2, 23, 7, "W", "D"); E(2, 27, 7, "E", "D")
E(2, 25, 6, "S", "w"); E(2, 24, 6, "S", "w"); E(2, 26, 6, "S", "w"); E(2, 26, 9, "N", "W"); E(2, 23, 9, "N", "w")
E(2, 12, 25, "S", "w"); E(2, 14, 25, "S", "w"); E(2, 11, 26, "W", "w"); E(2, 11, 29, "W", "w")
E(2, 12, 30, "N", "w"); E(2, 15, 30, "N", "w"); E(2, 16, 29, "E", "w")
for j in (17, 18, 19):
    E(2, 44, j, "E", "!")
# зубцы: наружный край хода
for j in range(8, 35):
    E(2, 6, j, "W", "P")
for i in range(6, 43):
    E(2, i, 34, "N", "P")
for j in range(8, 33):
    if j not in (17, 18, 19):
        E(2, 44, j, "E", "P")
for i in list(range(8, 23)) + list(range(28, 43)):
    E(2, i, 6, "S", "P")
# СВ: срезанная площадка — зубцы с запада и юга, на С и В два пролома в канал рва
E(2, 43, 35, "W", "P"); E(2, 43, 36, "W", "P"); E(2, 45, 33, "S", "P"); E(2, 46, 33, "S", "P")
E(2, 43, 36, "N", "P"); E(2, 44, 36, "N", "!"); E(2, 45, 36, "N", "!"); E(2, 46, 36, "N", "P")
E(2, 46, 33, "E", "P"); E(2, 46, 34, "E", "!"); E(2, 46, 35, "E", "!"); E(2, 46, 36, "E", "P")

# ---- L3
room(3, "q", "Штаб", "wood", "plaster", "wood", plain=1)
room(3, "l", "Пост лучника", "wood", "stone", "wood", plain=1)
rect(3, 11, 16, 25, 30, "q")
rect(3, 4, 7, 4, 7, "l")
room(3, "u", "Лебёдочная", "wood", "stone", "wood", plain=1)
rect(3, 23, 27, 6, 9, "u")
E(3, 24, 6, "S", "w"); E(3, 25, 6, "S", "w"); E(3, 26, 6, "S", "w"); E(3, 25, 9, "N", "w"); E(3, 27, 8, "E", "w")
E(3, 11, 26, "W", "w"); E(3, 12, 25, "S", "w"); E(3, 15, 25, "S", "w"); E(3, 16, 29, "E", "w")
E(3, 13, 30, "N", "w"); E(3, 15, 30, "N", "w")
E(3, 4, 4, "S", "w"); E(3, 4, 6, "W", "w"); E(3, 4, 7, "N", "w"); E(3, 7, 7, "E", "w")

# ---- L4
room(4, "o", "Дозорная", "wood", "plaster", "wood", plain=1)
rect(4, 11, 16, 25, 30, "o")
rect(4, 4, 7, 4, 7, ":")
rect(4, 23, 27, 6, 9, ":")                          # площадка надвратной
for i in range(23, 28):
    E(4, i, 6, "S", "P"); E(4, i, 9, "N", "P")
for j in range(6, 10):
    E(4, 23, j, "W", "P"); E(4, 27, j, "E", "P")
E(4, 12, 25, "S", "W"); E(4, 14, 25, "S", "W"); E(4, 11, 26, "W", "W"); E(4, 13, 30, "N", "W"); E(4, 16, 29, "E", "W")
for t in range(4, 8):
    E(4, t, 4, "S", "P"); E(4, t, 7, "N", "P"); E(4, 4, t, "W", "P"); E(4, 7, t, "E", "P")

# ---- L5: крыша донжона, башенка над лестницей
room(5, "x", "Башенка", "wood", "stone", "wood", plain=1)
rect(5, 11, 16, 25, 30, ":")
rect(5, 14, 16, 25, 27, "x")
roof(5, "roof", "Roof_Tower_RoundTiles", (15.5, 26.5), 0, " s=1.3 sy=1.15")
E(5, 14, 26, "W", "D"); E(5, 15, 25, "S", "w"); E(5, 16, 26, "E", "w")
for j in range(25, 31):
    E(5, 11, j, "W", "P")
for i in range(11, 17):
    E(5, i, 30, "N", "P")
for i in (11, 12, 13):
    E(5, i, 25, "S", "P")
for j in (28, 29):
    E(5, 16, j, "E", "P")

# ---- вертикали
stair(0, 16, 26, "^", 2)                             # зал -> оружейная, вдоль восточной
stair(1, 11, 29, "v", 2)                             # галерея зала -> казарма, вдоль западной
stair(2, 16, 26, "^", 2)                             # казарма -> штаб, вдоль восточной
stair(3, 11, 29, "v", 2)                             # штаб -> дозорная, вдоль западной
stair(4, 16, 27, "v", 2)                             # дозорная -> башенка на крыше, вдоль восточной
stair(-1, 22, 29, "^", 2)
stair(-1, 15, 11, "^", 3)
stair(0, 29, 8, ">", 3); stair(1, 33, 8, ">", 3)
stair(0, 42, 15, "^", 2); stair(1, 42, 18, "^", 2)
stair(1, 28, 30, "^", 3)
stair(0, 7, 24, "v", 2)                              # ход в галерею -> галерея
stair(1, 4, 5, "^", 2)                               # ЮЗ: нижний ярус -> ярус хода
stair(2, 6, 4, "<", 2)
put(2, 4, 4, "#"); put(3, 23, 9, "#")                               # ЮЗ: ярус хода -> пост лучника
stair(3, 5, 7, ">", 2)                               # ЮЗ: пост -> площадка
stair(2, 24, 9, ">", 2)                              # надвратная -> лебёдочная
stair(3, 23, 7, "^", 2)                              # лебёдочная -> площадка
hatch(-1, 11, 28, "N", "c"); bolt(-1, 11, 28, "U", "U")   # люк погреба в зал: засов сверху (петля A)
hatch(-1, 20, 7, "S", "x")                           # сток -> устье в куртине
put(0, 17, 30, "H", "k")                             # очаг кухни у стены донжона
for k in (1, 2, 3, 4):
    put(k, 17, 30, "H", "SHAFT")
put(5, 17, 30, " ", ("HOLE", "OUT", (16, 30), frozenset([(17, 30)]), "ladder"))
for i in (33, 34, 35):
    E(1, i, 8, "N", "!")
for j in (18, 19):
    E(1, 42, j, "W", "!")
E(2, 42, 20, "W", "r"); E(2, 42, 20, "N", "r")


# выходы из стен (имя, уровень, клетка, сторона): последний пересечённый — метка побега на финише
EXITS = [("Ворота", 0, 25, 6, "S"), ("Калитка", 0, 26, 34, "N"), ("Сток", 0, 20, 6, "S")] + \
        [("Стена", 2, 44, j, "E") for j in (17, 18, 19)] + \
        [("Стена", 2, 44, 36, "N"), ("Стена", 2, 45, 36, "N"), ("Стена", 2, 46, 34, "E"), ("Стена", 2, 46, 35, "E")]


# =========================================================================================== вывод
def write():
    i0, j0, i1, j1 = CROP
    W, H = i1 - i0 + 1, j1 - j0 + 1
    pts = [("зал", 0, 13, 27), ("двор", 0, 20, 12), ("стена", 2, 30, 7), ("крыша", 5, 12, 27),
           ("каменоломня", -1, 12, 12), ("кухня", 0, 19, 31), ("пристань", 0, 26, 35), ("цистерна", -1, 20, 16)]
    with open(os.path.join(HERE, "site.txt"), "w", encoding="utf-8") as f:
        f.write("name Крепость-тюрьма\n")
        f.write(f"size {W} {H}\n")
        f.write(f"origin {-W} {-H}\n")
        f.write(f"spawn L-1 {9 - i0},{30 - j0}\n")
        for (n, k, i, j) in pts:
            f.write(f"point {n} L{k} {i - i0},{j - j0}\n")
        f.write("grass 3\n")
        f.write("plain_solid 1\n")
        for (n, k, i, j, d) in EXITS:
            f.write(f"exit {n} L{k} {i - i0},{j - j0} {d}\n")
        f.write("finish border\n")
    for k in LEVELS:
        with open(os.path.join(HERE, f"L{k}.txt"), "w", encoding="utf-8") as f:
            f.write(f"# сгенерировано _gen.py — правь генератор, не карту\n")
            for ln in rooms[k] + roofs[k] + bolts[k]:
                f.write(ln + "\n")
            f.write("map\n")
            f.write("\n".join(emit(k)) + "\n")


def write_dressing():
    from _dress import DRESS
    i0, j0 = CROP[0], CROP[1]
    with open(os.path.join(HERE, "dressing.txt"), "w", encoding="utf-8") as f:
        f.write("# сгенерировано _gen.py из _dress.py — правь их, не этот файл\n")
        for (pid, m, k, i, j, an, kv) in DRESS:
            head = f"{pid}:{m}" if pid else m
            f.write(f"{head} L{k} {i - i0},{j - j0} {an} {kv}".rstrip() + "\n")


def write_guards():
    from _guards import GUARDS
    i0, j0 = CROP[0], CROP[1]
    def shift(tok):
        if "," in tok and tok.replace(",", "").replace("-", "").isdigit():
            i, j = tok.split(",")
            return f"{int(i) - i0},{int(j) - j0}"
        return tok
    with open(os.path.join(HERE, "guards.txt"), "w", encoding="utf-8") as f:
        f.write("# сгенерировано _gen.py из _guards.py — правь их, не этот файл\n")
        for ln in GUARDS.strip().split("\n"):
            s = ln.split("#", 1)[0].rstrip()
            if s.strip():
                f.write(" ".join(shift(t) for t in s.split()) + "\n")


if __name__ == "__main__":
    sys.path.insert(0, HERE)
    write()
    write_dressing()
    write_guards()
    print("карты записаны:", HERE)
