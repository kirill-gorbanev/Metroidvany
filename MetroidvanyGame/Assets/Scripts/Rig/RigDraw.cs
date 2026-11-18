using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fortress.Rig
{
    /// <summary>Отладочные линии рига прямо в игровом окне (G на стенде): цели ног, опора, центр масс.</summary>
    public static class RigDraw
    {
        public static bool enabled = true;
        static readonly List<(Vector3 a, Vector3 b, Color c)> lines = new List<(Vector3, Vector3, Color)>();
        static int frame = -1;
        static Material mat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            RenderPipelineManager.endCameraRendering -= Draw;
            RenderPipelineManager.endCameraRendering += Draw;
        }

        public static void Line(Vector3 a, Vector3 b, Color c)
        {
            if (!enabled) return;
            if (frame != Time.frameCount) { lines.Clear(); frame = Time.frameCount; }
            lines.Add((a, b, c));
        }

        public static void Cross(Vector3 p, float r, Color c)
        {
            Line(p - Vector3.right * r, p + Vector3.right * r, c);
            Line(p - Vector3.forward * r, p + Vector3.forward * r, c);
            Line(p, p + Vector3.up * r, c);
        }

        static void Draw(ScriptableRenderContext ctx, Camera cam)
        {
            if (!enabled || cam.cameraType != CameraType.Game || lines.Count == 0) return;
            if (frame < Time.frameCount - 1) return;          // устаревшие
            if (!mat)
            {
                mat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                mat.SetInt("_ZTest", (int)CompareFunction.Always);
                mat.SetInt("_ZWrite", 0);
                mat.SetInt("_Cull", (int)CullMode.Off);
            }
            mat.SetPass(0);
            GL.PushMatrix();
            GL.LoadProjectionMatrix(cam.projectionMatrix);
            GL.modelview = cam.worldToCameraMatrix;
            GL.Begin(GL.LINES);
            foreach (var (a, b, c) in lines) { GL.Color(c); GL.Vertex(a); GL.Vertex(b); }
            GL.End();
            GL.PopMatrix();
        }
    }
}
