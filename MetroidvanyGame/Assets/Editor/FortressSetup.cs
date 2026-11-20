using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Контрольный снимок кадра из редактора: <c>FortressSetup.Shot(path, camPos, lookAt)</c>.
/// Им пользуются инструменты в tools/unity (burst.py, envrun.py, hero_*.py).
/// </summary>
public static class FortressSetup
{
    public static void Shot(string path, Vector3 pos, Vector3 look)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var go = new GameObject("_shot"); var cam = go.AddComponent<Camera>();
        cam.transform.position = pos; cam.transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
        cam.fieldOfView = 62f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 900f;
        var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        var req = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req); else cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = null; cam.targetTexture = null;
        Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
    }
}
