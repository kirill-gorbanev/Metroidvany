using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>ВЫСТУП — камень, край, неровность стены. Короткий зацеп по верхней грани кубика (длинная горизонтальная ось,
    /// с концов урезан на inset). Отличия от поручня — в блоке HeroProto.Ledge: прилипает только рядом (малый радиус,
    /// пол рядом не крадётся), поручень рядом сильнее; на выступе можно подтянуться (вис), но не перевалить через него.
    /// Ставится маркером сборщиком арены (не по геометрии: ступени и бордюры выступами не становятся). Корень — декор:
    /// не опора для тела и не стена для кисти.</summary>
    public class ProtoNub : ProtoHold
    {
        [Tooltip("Насколько урезать отрезок с концов (м мира).")]
        public float inset = 0.03f;
        [Tooltip("Ступенька: тело может встать на камень (не сползает). Выкл — камень только для руки, как раньше.")]
        public bool foothold = true;

        void Awake() => Fit();
        void OnValidate() => Fit();

        /// <summary>Отрезок — по верхней грани единичного кубика вдоль его длинной горизонтальной оси.</summary>
        public void Fit()
        {
            Vector3 s = transform.lossyScale;
            bool alongX = Mathf.Abs(s.x) >= Mathf.Abs(s.z);
            float len = Mathf.Abs(alongX ? s.x : s.z);
            float half = Mathf.Max(0.5f - inset / Mathf.Max(len, 1e-3f), 0f);
            a = alongX ? new Vector3(-half, 0.5f, 0f) : new Vector3(0f, 0.5f, -half);
            b = alongX ? new Vector3(half, 0.5f, 0f) : new Vector3(0f, 0.5f, half);
        }

        void OnDrawGizmos() { Gizmos.color = new Color(1f, 0.55f, 0.15f); Gizmos.DrawLine(A, B); }
    }
}
