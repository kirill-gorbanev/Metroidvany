using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fortress.Hero.Arm2
{
    /// <summary>Запись ввода автора: шапка (сцена, герой, поза тела на старте) + мышь покадрово.</summary>
    public class Arm2Recording
    {
        public string scene, hero, path;
        public Vector3 bodyPos; public Quaternion bodyRot = Quaternion.identity;
        public bool hasCam; public float camYaw, camPitch, camDist;       // вид камеры на старте записи
        public readonly List<Arm2InputFrame> frames = new List<Arm2InputFrame>();
        public float Duration { get { float t = 0f; foreach (var f in frames) t += f.dt; return t; } }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "../../tools/herosim/rec"));

        public void Save(string file)
        {
            var sb = new StringBuilder();
            sb.AppendFormat(Inv, "arm2rec 1|{0}|{1}|{2:R} {3:R} {4:R}|{5:R} {6:R} {7:R} {8:R}|{9:R} {10:R} {11:R}\n", scene, hero,
                bodyPos.x, bodyPos.y, bodyPos.z, bodyRot.x, bodyRot.y, bodyRot.z, bodyRot.w, camYaw, camPitch, camDist);
            foreach (var f in frames)
                sb.AppendFormat(Inv, "{0:R};{1:R};{2:R};{3:R};{4:R};{5};{6:R};{7};{8}\n", f.dt, f.pos.x, f.pos.y, f.delta.x, f.delta.y,
                    (f.lmb ? 1 : 0) | (f.lmbDown ? 2 : 0) | (f.rmb ? 4 : 0), f.scroll, f.screenW, f.screenH);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, sb.ToString());
            path = file;
        }

        public static Arm2Recording Load(string file)
        {
            var lines = File.ReadAllLines(file);
            var r = new Arm2Recording { path = file };
            var h = lines[0].Split('|');
            if (h.Length < 5 || !h[0].StartsWith("arm2rec")) throw new InvalidDataException("не запись ввода: " + file);
            r.scene = h[1]; r.hero = h[2];
            var p = h[3].Split(' '); var q = h[4].Split(' ');
            r.bodyPos = new Vector3(F(p[0]), F(p[1]), F(p[2]));
            r.bodyRot = new Quaternion(F(q[0]), F(q[1]), F(q[2]), F(q[3]));
            if (h.Length > 5) { var c5 = h[5].Split(' '); r.camYaw = F(c5[0]); r.camPitch = F(c5[1]); r.camDist = F(c5[2]); r.hasCam = r.camDist > 0f; }
            for (int i = 1; i < lines.Length; i++)
            {
                var c = lines[i].Split(';'); if (c.Length < 9) continue;
                int b = int.Parse(c[5], Inv);
                r.frames.Add(new Arm2InputFrame
                {
                    dt = F(c[0]), pos = new Vector2(F(c[1]), F(c[2])), delta = new Vector2(F(c[3]), F(c[4])),
                    lmb = (b & 1) != 0, lmbDown = (b & 2) != 0, rmb = (b & 4) != 0, scroll = F(c[6]),
                    screenW = int.Parse(c[7], Inv), screenH = int.Parse(c[8], Inv)
                });
            }
            return r;
        }
        static float F(string s) => float.Parse(s, Inv);

        /// <summary>Самая свежая запись в папке (или null).</summary>
        public static string Latest()
        {
            if (!Directory.Exists(Dir)) return null;
            string best = null; System.DateTime t = System.DateTime.MinValue;
            foreach (var f in Directory.GetFiles(Dir, "*.txt")) { var w = File.GetLastWriteTimeUtc(f); if (w > t) { t = w; best = f; } }
            return best;
        }
    }

    /// <summary>ЗАПИСЬ ВВОДА (инструмент). Автор играет — F9 начинает и заканчивает запись мыши покадрово в tools/herosim/rec.
    /// Работает в любой сцене с героем — старым (HeroProto) и новым (Arm2Hero): одна и та же запись проигрывается на обоих,
    /// таблица «старое / новое» считается по вводу автора. Сам ничего не двигает.</summary>
    /// Только в редакторе и только в сцене с героем; запись начинать, когда тело стоит (скорость в шапку не пишется).
    [DefaultExecutionOrder(1000)]                                        // шаг физики следа — после шага героя
    public class Arm2Recorder : MonoBehaviour
    {
        static Arm2Recorder live;
        Arm2Recording rec; string lastSaved; float savedAt = -99f;
        public static bool Recording => live && live.rec != null;

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (live) return;
            if (!FindAnyObjectByType<Arm2Hero>() && !FindAnyObjectByType<Proto.HeroProto>()) return;     // сцена без героя — не нужен
            live = new GameObject("Arm2Recorder (F9 — запись ввода)").AddComponent<Arm2Recorder>();       // объект сцены: уходит вместе с Play
        }
#endif

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f9Key.wasPressedThisFrame) { if (rec == null) Begin(); else End(); }
            if (rec != null) rec.frames.Add(Arm2Input.ReadMouse());
        }

        // СЛЕД СОСТОЯНИЯ (2026-11-27): вместе с вводом пишется то, что было НА САМОМ ДЕЛЕ, по шагам физики — тело, кисть, цель,
        // хват, причина отказа хвата. Запись, начатая с зажатой ЛКМ или в хвате, повтором не воспроизводится (состояние на старте
        // в шапке не хранится) — а след читается всегда. Файл рядом с записью: *_state.csv.
        StringBuilder trace; float traceT;
        static readonly CultureInfo TInv = CultureInfo.InvariantCulture;
        void FixedUpdate()
        {
            if (rec == null || trace == null) return;
            var h = Arm2Hero.Live; if (!h || !h.body || !h.arm || !h.grip) return;
            var st = h.state; traceT += Time.fixedDeltaTime;
            Vector3 b = h.body.Rb.position, hd = h.arm.HandPos, e = h.arm.ElbowPos, c = st.cursorPoint, tg = st.handTarget, g = h.arm.GhostPos, gp = st.gripPoint;
            trace.AppendFormat(TInv, "{0:F3};{1:F3};{2:F3};{3:F3};{4:F1};{5:F1};{6:F3};{7:F3};{8:F3};{9:F3};{10:F3};{11:F3};{12:F3};{13:F3};{14:F3};{15:F3};{16:F3};{17:F3};{18:F3};{19:F3};{20:F3};",
                traceT, b.x, b.y, b.z, h.body.Yaw(0f), Vector3.Angle(h.body.Rb.rotation * Vector3.up, Vector3.up), hd.x, hd.y, hd.z, e.x, e.y, e.z, c.x, c.y, c.z, tg.x, tg.y, tg.z, g.x, g.y, g.z);
            trace.AppendFormat(TInv, "{0};{1};{2};{3};{4};{5};{6};{7:F3};{8:F3};{9:F3};{10:F2};{11:F3};{12};{13};{14:F3};{15:F3};{16:F3};{17:F0}\n",
                st.frame.lmb ? 1 : 0, st.wantGrip ? 1 : 0, st.gripped ? 1 : 0, st.gripOnHold ? 1 : 0, st.hanging ? 1 : 0, h.grip.Slipping ? 1 : 0, h.arm.Climbing ? 1 : 0,
                gp.x, gp.y, gp.z, st.gripped ? st.gripNormal.y : 0f, st.pullLength, st.heldItem ? st.heldItem.name.Replace(';', ' ') : "-", Arm2Grip.Last,
                st.heldItem ? st.heldItem.position.x : 0f, st.heldItem ? st.heldItem.position.y : 0f, st.heldItem ? st.heldItem.position.z : 0f,
                st.heldItem ? Vector3.Angle(st.heldItem.rotation * Vector3.up, Vector3.up) : 0f);
            Arm2Grip.Last = "-";
        }
        const string TraceHead = "t;bx;by;bz;yaw;tilt;hx;hy;hz;ex;ey;ez;cx;cy;cz;tx;ty;tz;gx;gy;gz;lmb;want;grip;hold;hang;slip;climb;px;py;pz;pn;plen;item;why;ix;iy;iz;itilt\n";

        void Begin()
        {
            rec = new Arm2Recording { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, hero = "?" };
            trace = new StringBuilder(TraceHead); traceT = 0f; Arm2Grip.Last = "-";
            if (Arm2Hero.Live && Arm2Hero.Live.body)
            {
                var h = Arm2Hero.Live; rec.hero = "Arm2";
                rec.bodyPos = h.body.Rb.position; rec.bodyRot = h.body.Rb.rotation;                       // поза физики, не интерполированная
                if (h.cam) { rec.hasCam = true; rec.camYaw = h.cam.yaw; rec.camPitch = h.cam.pitch; rec.camDist = h.cam.distance; }
            }
            else
            {
                var old = FindAnyObjectByType<Proto.HeroProto>();
                if (old && old.bodyCube) { rec.hero = "Proto"; rec.bodyPos = old.bodyCube.position; rec.bodyRot = old.bodyCube.rotation; }
                var oc = FindAnyObjectByType<Proto.ProtoOrbitCamera>();
                if (oc) { rec.hasCam = true; rec.camYaw = oc.yaw; rec.camPitch = oc.pitch; rec.camDist = oc.distance; }
            }
        }

        void End()
        {
            string file = Path.Combine(Arm2Recording.Dir, $"rec_{System.DateTime.Now:yyyyMMdd_HHmmss}_{rec.scene}_{rec.hero}.txt");
            rec.Save(file);
            if (trace != null && trace.Length > TraceHead.Length) File.WriteAllText(file.Substring(0, file.Length - 4) + "_state.csv", trace.ToString());
            trace = null;
            lastSaved = $"{Path.GetFileName(file)} — {rec.frames.Count} кадров, {rec.Duration:F1} с";
            savedAt = Time.unscaledTime;
            Debug.Log("[Arm2] запись ввода: " + file);
            rec = null;
        }

        void OnGUI()
        {
            if (rec == null && Time.unscaledTime - savedAt > 4f) return;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            st.normal.textColor = rec != null ? new Color(1f, 0.35f, 0.3f) : Color.white;
            GUI.Label(new Rect(12f, Screen.height - 34f, 900f, 28f),
                rec != null ? $"● запись ввода  {rec.frames.Count} кадров  (F9 — стоп)" : "запись сохранена: " + lastSaved, st);
        }
    }
}
