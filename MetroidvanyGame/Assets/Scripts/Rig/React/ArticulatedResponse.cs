using UnityEngine;

namespace Fortress.Rig.React
{
    /// <summary>
    /// Мгновенный отклик сочленённого тела на импульс — «тензоры → реакции».
    ///
    /// Тело — дерево из N частей (0 — корень, таз). Координаты движения q̇: 6 у корня (сдвиг + поворот вокруг осей мира)
    /// и по 3 у каждого сустава (поворот части относительно родителя вокруг осей мира, в точке сустава).
    /// Для точки p на части b якобиан Jv(p) (3×dof) переводит q̇ в скорость точки:
    ///   сдвиг корня → E;  поворот вокруг оси e в шарнире c (корень или сустав на пути b→корень) → e × (p − c).
    /// Матрица масс в этих координатах:  M = Σᵢ ( mᵢ·Jv(comᵢ)ᵀJv(comᵢ) + Jωᵢᵀ·Iᵢ·Jωᵢ ),  Iᵢ = Rᵢ·diag(Iлок)·Rᵢᵀ — тензор инерции части в мире.
    /// Импульс J в точке p меняет скорости так:  Δq̇ = M⁻¹·Jv(p)ᵀ·J   (точно для свободного сочленённого тела в этот миг).
    /// Эффективная обратная масса тела в точке p вдоль n:  w = nᵀ·Jv·M⁻¹·Jvᵀ·n  — удар в кисть «лёгкий» (рука отлетает),
    /// в грудь — «тяжёлый» (качнётся всё тело). По w решается контакт с предметом: λ = −vₙ / (w_тела + w_предмета).
    /// Размер: 13 частей → dof = 6 + 3·12 = 42; сборка M ~25 тыс. умножений, разложение Холецкого ~12 тыс. — микросекунды.
    /// </summary>
    public sealed class ArticulatedResponse
    {
        public readonly int N, Dof;
        public readonly int[] Parent;          // родитель части (−1 у корня)
        public readonly float[] Mass;
        public readonly Vector3[] InertiaDiag;  // главные моменты инерции (в осях части с поворотом inertiaRotation)
        public readonly Quaternion[] InertiaRot;

        // поза на этот миг (мир)
        public readonly Vector3[] Com;
        public readonly Quaternion[] Rot;
        public readonly Vector3[] Pivot;       // точка сустава части с родителем; у корня — центр масс корня
        /// <summary>Сопротивление сустава за время удара (кг·м²): h·D + h²·K мышцы (h — длительность удара).
        /// Тонус высокий — тело принимает удар целиком; низкий (оглушён, спит) — конечности болтаются. Индекс — часть (у корня не используется).</summary>
        public readonly float[] Impedance;

        readonly bool[,] onPath;               // onPath[b, c] — шарнир c на пути от части b к корню (включая саму b)
        readonly double[] M, L;                // dof×dof
        readonly double[] tmp, tmp2;
        bool factored;

        public ArticulatedResponse(int[] parent, float[] mass, Vector3[] inertiaDiag, Quaternion[] inertiaRot)
        {
            N = parent.Length;
            Dof = 6 + 3 * (N - 1);
            Parent = parent; Mass = mass; InertiaDiag = inertiaDiag; InertiaRot = inertiaRot;
            Com = new Vector3[N]; Rot = new Quaternion[N]; Pivot = new Vector3[N]; Impedance = new float[N];
            onPath = new bool[N, N];
            for (int b = 0; b < N; b++)
                for (int c = b; c >= 0 && c < N; c = Parent[c]) { onPath[b, c] = true; if (Parent[c] < 0) break; }
            M = new double[Dof * Dof]; L = new double[Dof * Dof];
            tmp = new double[Dof]; tmp2 = new double[Dof];
        }

        /// <summary>Первая координата шарнира c (корень — 3 поворотные после 3 сдвиговых).</summary>
        public int RotIndex(int c) => c == 0 ? 3 : 6 + 3 * (c - 1);

        /// <summary>Якобиан точки p на части b: столбец k → скорость точки (3 числа). Заполняет jv[3*dof].</summary>
        public void PointJacobian(int b, Vector3 p, double[] jv)
        {
            System.Array.Clear(jv, 0, 3 * Dof);
            // сдвиг корня
            jv[0 * Dof + 0] = 1; jv[1 * Dof + 1] = 1; jv[2 * Dof + 2] = 1;
            for (int c = b; c >= 0; c = Parent[c])
            {
                Vector3 r = p - Pivot[c];
                int k = RotIndex(c);
                // e_x × r = (0, −r.z, r.y); e_y × r = (r.z, 0, −r.x); e_z × r = (−r.y, r.x, 0)
                jv[0 * Dof + k] = 0;    jv[1 * Dof + k] = -r.z; jv[2 * Dof + k] = r.y;
                jv[0 * Dof + k + 1] = r.z; jv[1 * Dof + k + 1] = 0; jv[2 * Dof + k + 1] = -r.x;
                jv[0 * Dof + k + 2] = -r.y; jv[1 * Dof + k + 2] = r.x; jv[2 * Dof + k + 2] = 0;
                if (Parent[c] < 0) break;
            }
        }

        double[] jvBuf;

        /// <summary>Собрать M по текущей позе (Com, Rot, Pivot) и разложить (Холецкий). Вызывать раз в шаг, если были удары.</summary>
        public void Build()
        {
            if (jvBuf == null) jvBuf = new double[3 * Dof];
            System.Array.Clear(M, 0, M.Length);
            for (int i = 0; i < N; i++)
            {
                // линейная часть: m·Jvᵀ·Jv в центре масс части
                PointJacobian(i, Com[i], jvBuf);
                double m = Mass[i];
                for (int a = 0; a < Dof; a++)
                {
                    double ax = jvBuf[a], ay = jvBuf[Dof + a], az = jvBuf[2 * Dof + a];
                    if (ax == 0 && ay == 0 && az == 0) continue;
                    for (int c = a; c < Dof; c++)
                    {
                        double v = ax * jvBuf[c] + ay * jvBuf[Dof + c] + az * jvBuf[2 * Dof + c];
                        if (v != 0) M[a * Dof + c] += m * v;
                    }
                }
                // вращательная часть: Jωᵀ·I·Jω, Jω — единичные оси у шарниров на пути (корень и суставы)
                Quaternion q = Rot[i] * InertiaRot[i];
                Matrix4x4 R = Matrix4x4.Rotate(q);
                Vector3 d = InertiaDiag[i];
                var I = new double[3, 3];
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c < 3; c++)
                        I[r, c] = R[r, 0] * d.x * R[c, 0] + R[r, 1] * d.y * R[c, 1] + R[r, 2] * d.z * R[c, 2];
                for (int ca = i; ca >= 0; ca = Parent[ca])
                {
                    int ka = RotIndex(ca);
                    for (int cb = i; cb >= 0; cb = Parent[cb])
                    {
                        int kb = RotIndex(cb);
                        for (int r = 0; r < 3; r++)
                            for (int c = 0; c < 3; c++)
                            {
                                int a = ka + r, bcol = kb + c;
                                if (bcol >= a) M[a * Dof + bcol] += I[r, c];
                            }
                        if (Parent[cb] < 0) break;
                    }
                    if (Parent[ca] < 0) break;
                }
            }
            // мышцы держат суставы за время удара: неявный шаг (M + h·D + h²·K)
            for (int i = 1; i < N; i++)
            {
                int k = RotIndex(i);
                for (int r = 0; r < 3; r++) M[(k + r) * Dof + k + r] += Impedance[i];
            }
            // симметрия + разложение Холецкого (с малой регуляризацией)
            for (int a = 0; a < Dof; a++) for (int c = 0; c < a; c++) M[a * Dof + c] = M[c * Dof + a];
            factored = Cholesky();
        }

        bool Cholesky()
        {
            System.Array.Copy(M, L, M.Length);
            for (int j = 0; j < Dof; j++)
            {
                double s = L[j * Dof + j] + 1e-6;
                for (int k = 0; k < j; k++) s -= L[j * Dof + k] * L[j * Dof + k];
                if (s <= 0) return false;
                double ljj = System.Math.Sqrt(s);
                L[j * Dof + j] = ljj;
                for (int i = j + 1; i < Dof; i++)
                {
                    double t = L[i * Dof + j];
                    for (int k = 0; k < j; k++) t -= L[i * Dof + k] * L[j * Dof + k];
                    L[i * Dof + j] = t / ljj;
                }
                for (int i = 0; i < j; i++) L[i * Dof + j] = 0;
            }
            return true;
        }

        /// <summary>x = M⁻¹·b (на месте).</summary>
        public void Solve(double[] b)
        {
            if (!factored) { System.Array.Clear(b, 0, Dof); return; }
            for (int i = 0; i < Dof; i++)
            {
                double s = b[i];
                for (int k = 0; k < i; k++) s -= L[i * Dof + k] * b[k];
                b[i] = s / L[i * Dof + i];
            }
            for (int i = Dof - 1; i >= 0; i--)
            {
                double s = b[i];
                for (int k = i + 1; k < Dof; k++) s -= L[k * Dof + i] * b[k];
                b[i] = s / L[i * Dof + i];
            }
        }

        /// <summary>Отклик на импульс J (Н·с) в точке p части b: прибавляет Δq̇ к dq.</summary>
        public void ApplyImpulse(int b, Vector3 p, Vector3 J, double[] dq)
        {
            if (jvBuf == null) jvBuf = new double[3 * Dof];
            PointJacobian(b, p, jvBuf);
            for (int k = 0; k < Dof; k++) tmp[k] = jvBuf[k] * J.x + jvBuf[Dof + k] * J.y + jvBuf[2 * Dof + k] * J.z;
            Solve(tmp);
            for (int k = 0; k < Dof; k++) dq[k] += tmp[k];
        }

        // ---------------------------------------------------------------- опоры: части, «приваренные» к полу
        double[] jc;          // якобиан опор: 6 строк на опору (скорость центра и угловая скорость части), × dof
        double[] minvJcT;     // M⁻¹·J_cᵀ: dof × (6·k)
        double[] A;           // J_c·M⁻¹·J_cᵀ: (6k)×(6k)
        int[] supports = new int[0];
        int nc;

        /// <summary>Задать опорные части (стопы на полу). Вызывать после Build(), если опоры есть.</summary>
        public void SetSupports(int[] bodies)
        {
            supports = bodies ?? new int[0];
            nc = 6 * supports.Length;
            if (nc == 0 || !factored) return;
            if (jvBuf == null) jvBuf = new double[3 * Dof];
            if (jc == null || jc.Length < nc * Dof) { jc = new double[nc * Dof]; minvJcT = new double[Dof * nc]; A = new double[nc * nc]; }
            System.Array.Clear(jc, 0, nc * Dof);
            for (int s = 0; s < supports.Length; s++)
            {
                int b = supports[s];
                PointJacobian(b, Com[b], jvBuf);
                for (int r = 0; r < 3; r++) System.Array.Copy(jvBuf, r * Dof, jc, (6 * s + r) * Dof, Dof);
                // угловая скорость части: единичные оси у шарниров на пути к корню
                for (int c = b; c >= 0; c = Parent[c])
                {
                    int k = RotIndex(c);
                    for (int r = 0; r < 3; r++) jc[(6 * s + 3 + r) * Dof + k + r] = 1;
                    if (Parent[c] < 0) break;
                }
            }
            // M⁻¹·J_cᵀ по столбцам и A = J_c·M⁻¹·J_cᵀ (+ регуляризация), разложение A на месте
            for (int i = 0; i < nc; i++)
            {
                for (int k = 0; k < Dof; k++) tmp[k] = jc[i * Dof + k];
                Solve(tmp);
                for (int k = 0; k < Dof; k++) minvJcT[k * nc + i] = tmp[k];
            }
            for (int i = 0; i < nc; i++)
                for (int j = 0; j < nc; j++)
                {
                    double v = 0;
                    for (int k = 0; k < Dof; k++) v += jc[i * Dof + k] * minvJcT[k * nc + j];
                    A[i * nc + j] = v + (i == j ? 1e-7 : 0);
                }
            CholeskyInPlace(A, nc);
        }

        static void CholeskyInPlace(double[] a, int n)
        {
            for (int j = 0; j < n; j++)
            {
                double s = a[j * n + j];
                for (int k = 0; k < j; k++) s -= a[j * n + k] * a[j * n + k];
                double ljj = System.Math.Sqrt(System.Math.Max(s, 1e-12));
                a[j * n + j] = ljj;
                for (int i = j + 1; i < n; i++)
                {
                    double t = a[i * n + j];
                    for (int k = 0; k < j; k++) t -= a[i * n + k] * a[j * n + k];
                    a[i * n + j] = t / ljj;
                }
                for (int i = 0; i < j; i++) a[i * n + j] = 0;
            }
        }

        static void SolveChol(double[] l, int n, double[] b)
        {
            for (int i = 0; i < n; i++) { double s = b[i]; for (int k = 0; k < i; k++) s -= l[i * n + k] * b[k]; b[i] = s / l[i * n + i]; }
            for (int i = n - 1; i >= 0; i--) { double s = b[i]; for (int k = i + 1; k < n; k++) s -= l[k * n + i] * b[k]; b[i] = s / l[i * n + i]; }
        }

        double[] lam;

        /// <summary>Убрать из скоростей u всё, что двигает опоры:  u ← u − M⁻¹J_cᵀ·(J_c M⁻¹ J_cᵀ)⁻¹·J_c·u.
        /// Так удар не «уносит» стоящего человека: стопы держат пол, гнутся колени и бёдра.</summary>
        public void ProjectSupports(double[] u)
        {
            if (nc == 0) return;
            if (lam == null || lam.Length < nc) lam = new double[nc];
            for (int i = 0; i < nc; i++)
            {
                double v = 0;
                for (int k = 0; k < Dof; k++) v += jc[i * Dof + k] * u[k];
                lam[i] = v;
            }
            SolveChol(A, nc, lam);
            for (int k = 0; k < Dof; k++)
            {
                double v = 0;
                for (int i = 0; i < nc; i++) v += minvJcT[k * nc + i] * lam[i];
                u[k] -= v;
            }
        }

        /// <summary>Отклик на импульс при стоящих опорах (SetSupports): Δq̇ = (M⁻¹ − M⁻¹J_cᵀ(J_cM⁻¹J_cᵀ)⁻¹J_cM⁻¹)·Jvᵀ·J.</summary>
        public void ApplyImpulseSupported(int b, Vector3 p, Vector3 J, double[] dq)
        {
            if (jvBuf == null) jvBuf = new double[3 * Dof];
            PointJacobian(b, p, jvBuf);
            var u = new double[Dof];
            for (int k = 0; k < Dof; k++) u[k] = jvBuf[k] * J.x + jvBuf[Dof + k] * J.y + jvBuf[2 * Dof + k] * J.z;
            Solve(u);
            ProjectSupports(u);
            for (int k = 0; k < Dof; k++) dq[k] += u[k];
        }

        /// <summary>Обратная эффективная масса тела в точке p части b вдоль n (1/кг).</summary>
        public float InvEffMass(int b, Vector3 p, Vector3 n)
        {
            if (jvBuf == null) jvBuf = new double[3 * Dof];
            PointJacobian(b, p, jvBuf);
            for (int k = 0; k < Dof; k++) { tmp[k] = jvBuf[k] * n.x + jvBuf[Dof + k] * n.y + jvBuf[2 * Dof + k] * n.z; tmp2[k] = tmp[k]; }
            Solve(tmp);
            double w = 0;
            for (int k = 0; k < Dof; k++) w += tmp2[k] * tmp[k];
            return (float)w;
        }

        /// <summary>Обратная эффективная масса в точке p части b вдоль n при стоящих опорах (SetSupports):
        /// w = nᵀ·Jv·(M⁻¹ − M⁻¹J_cᵀ(J_cM⁻¹J_cᵀ)⁻¹J_cM⁻¹)·Jvᵀ·n — удар в опорную стопу «упирается в пол» (w ≈ 0).</summary>
        public float InvEffMassSupported(int b, Vector3 p, Vector3 n)
        {
            if (jvBuf == null) jvBuf = new double[3 * Dof];
            PointJacobian(b, p, jvBuf);
            for (int k = 0; k < Dof; k++) { tmp[k] = jvBuf[k] * n.x + jvBuf[Dof + k] * n.y + jvBuf[2 * Dof + k] * n.z; tmp2[k] = tmp[k]; }
            Solve(tmp);
            ProjectSupports(tmp);
            double w = 0;
            for (int k = 0; k < Dof; k++) w += tmp2[k] * tmp[k];
            return (float)System.Math.Max(w, 0.0);
        }

        /// <summary>Скорость точки p части b от скоростей dq (дополнительная, «отклик»).</summary>
        public Vector3 PointVelocity(int b, Vector3 p, double[] dq)
        {
            if (jvBuf == null) jvBuf = new double[3 * Dof];
            PointJacobian(b, p, jvBuf);
            double x = 0, y = 0, z = 0;
            for (int k = 0; k < Dof; k++) { x += jvBuf[k] * dq[k]; y += jvBuf[Dof + k] * dq[k]; z += jvBuf[2 * Dof + k] * dq[k]; }
            return new Vector3((float)x, (float)y, (float)z);
        }

        /// <summary>Полная масса и тензор инерции всего тела относительно точки s (теорема Штейнера) — для качания вокруг опоры.</summary>
        public Matrix4x4 InertiaAbout(Vector3 s, out float total)
        {
            total = 0;
            var I = new double[3, 3];
            for (int i = 0; i < N; i++)
            {
                Matrix4x4 R = Matrix4x4.Rotate(Rot[i] * InertiaRot[i]);
                Vector3 d = InertiaDiag[i];
                Vector3 r = Com[i] - s;
                double m = Mass[i]; total += Mass[i];
                double rr = r.sqrMagnitude;
                for (int a = 0; a < 3; a++)
                    for (int c = 0; c < 3; c++)
                    {
                        double loc = R[a, 0] * d.x * R[c, 0] + R[a, 1] * d.y * R[c, 1] + R[a, 2] * d.z * R[c, 2];
                        double par = m * ((a == c ? rr : 0) - r[a] * r[c]);
                        I[a, c] += loc + par;
                    }
            }
            var o = Matrix4x4.identity;
            for (int a = 0; a < 3; a++) for (int c = 0; c < 3; c++) o[a, c] = (float)I[a, c];
            return o;
        }
    }
}
