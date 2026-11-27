using System.Collections.Generic;
using UnityEngine;

namespace Fortress.Hero.Arm2
{
    /// <summary>ПОДВИЖНАЯ ПЛАТФОРМА для физического героя: кинематический Rigidbody, двигается только MovePosition / MoveRotation
    /// в шаге физики — тогда трение везёт то, что на ней стоит, а сустав хвата может держаться за неё.
    /// (Старый ProtoMover двигал transform без Rigidbody — «стоять на платформе» делала логика.)
    /// Челнок: туда-обратно вдоль axis на travel с разгоном accel; spin — вращение вокруг вертикали, °/с.</summary>
    [DefaultExecutionOrder(-100)]   // платформы шагают раньше героя (-50) — и в игре, и в замере
    [RequireComponent(typeof(Rigidbody))]
    public class Arm2Mover : MonoBehaviour
    {
        public Vector3 axis = Vector3.right;
        public float travel = 2f, speed = 0.5f, accel = 1f, spin = 0f;

        public static readonly List<Arm2Mover> All = new List<Arm2Mover>();
        Rigidbody rb; Vector3 origin; Quaternion rot0; float s, v, dir = 1f, angle;
        /// <summary>Скорость платформы сейчас (для замеров).</summary>
        public Vector3 Velocity => axis.normalized * v;
        public Rigidbody Rb => rb;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate;
            origin = rb.position; rot0 = rb.rotation;
        }
        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        /// <summary>В начальную фазу (замер, повтор записи — повторяемость).</summary>
        public void ResetPhase()
        {
            s = 0f; v = 0f; dir = 1f; angle = 0f;
            rb.position = origin; rb.rotation = rot0; transform.SetPositionAndRotation(origin, rot0);
        }

        void FixedUpdate() { if (!Arm2Hero.Manual) FixedStep(Time.fixedDeltaTime); }

        public void FixedStep(float dt)
        {
            if (travel > 0f && speed > 0f)
            {
                float brake = v * v / (2f * Mathf.Max(0.01f, accel));                    // путь торможения
                if (dir > 0f && s + brake >= travel) dir = -1f; else if (dir < 0f && s - brake <= 0f) dir = 1f;
                v = Mathf.MoveTowards(v, dir * speed, accel * dt);
                s += v * dt;
                rb.MovePosition(origin + axis.normalized * s);
            }
            if (Mathf.Abs(spin) > 0.001f)
            {
                angle += spin * dt;
                rb.MoveRotation(rot0 * Quaternion.AngleAxis(angle, Vector3.up));
            }
        }
    }
}
