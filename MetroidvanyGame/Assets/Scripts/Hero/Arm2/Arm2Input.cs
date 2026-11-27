using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fortress.Hero.Arm2
{
    /// <summary>Ввод одного кадра. Единицы — как у старого героя: позиция и ход мыши в px (начало — левый низ).</summary>
    public struct Arm2InputFrame
    {
        public float dt;                 // время кадра, с
        public Vector2 pos, delta;       // курсор и ход мыши за кадр, px
        public bool lmb, lmbDown, rmb;   // ЛКМ зажата, нажата в этом кадре, ПКМ зажата
        public float scroll;             // колесо за кадр
        public int screenW, screenH;
    }

    /// <summary>БЛОК «ВВОД». Читает мышь раз в кадр (Update) и отдаёт Arm2InputFrame. Ничего не двигает.
    /// Источник можно подменить: бот и повтор записи кладут свои кадры в feed — так баг автора становится повторяемым тестом.</summary>
    public class Arm2Input : MonoBehaviour
    {
        /// <summary>Чужой источник ввода (бот, повтор записи). Пусто — мышь.</summary>
        public System.Func<Arm2InputFrame> feed;
        /// <summary>Очередь повтора записи «вживую» (смотреть глазами): кадры выдаются по их времени.</summary>
        readonly Queue<Arm2InputFrame> playback = new Queue<Arm2InputFrame>();
        float playbackClock; Arm2InputFrame lastPlayed;
        public bool PlayingBack => playback.Count > 0;

        public void StartPlayback(IEnumerable<Arm2InputFrame> frames)
        {
            playback.Clear(); playbackClock = 0f; lastPlayed = default;
            foreach (var f in frames) playback.Enqueue(f);
        }

        public Arm2InputFrame Read()
        {
            if (feed != null) return feed();
            if (playback.Count > 0) return ReadPlayback();
            return ReadMouse();
        }

        public static Arm2InputFrame ReadMouse()
        {
            var f = new Arm2InputFrame { dt = Time.unscaledDeltaTime, screenW = Screen.width, screenH = Screen.height };
            var m = Mouse.current;
            if (m == null) return f;
            f.pos = m.position.ReadValue(); f.delta = m.delta.ReadValue();
            f.lmb = m.leftButton.isPressed; f.lmbDown = m.leftButton.wasPressedThisFrame; f.rmb = m.rightButton.isPressed;
            f.scroll = m.scroll.ReadValue().y;
            return f;
        }

        /// <summary>Повтор вживую: за настоящий кадр выдаются все записанные кадры, чьё время прошло (ход мыши и колесо складываются).</summary>
        Arm2InputFrame ReadPlayback()
        {
            playbackClock += Time.unscaledDeltaTime;
            var acc = lastPlayed; acc.delta = Vector2.zero; acc.scroll = 0f; acc.lmbDown = false; acc.dt = Time.unscaledDeltaTime;   // база — последний выданный кадр, не будущий
            while (playback.Count > 0 && playback.Peek().dt <= playbackClock)
            {
                var f = playback.Dequeue(); playbackClock -= f.dt;
                acc.pos = f.pos; acc.delta += f.delta; acc.scroll += f.scroll; acc.lmb = f.lmb; acc.rmb = f.rmb; acc.lmbDown |= f.lmbDown;
                acc.screenW = f.screenW; acc.screenH = f.screenH;
            }
            lastPlayed = acc;
            return acc;
        }
    }
}
