"""Собирает шейдеры крепости для HDRP из HDRP/Lit установленной версии Unity.

Fortress/LitWorldUV, Fortress/LitVertexColor = HDRP/Lit + FortLitData.hlsl (мировые UV, цвет вершин). Файлы .shader перезаписываются на месте —
GUID (.meta) не меняются, материалы остаются на своих шейдерах.

Запуск после обновления Unity (версию брать из ProjectSettings/ProjectVersion.txt):
    python tools/gen_hdrp_shaders.py
"""
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
PROJ = ROOT / "MetroidvanyGame"
OUT = PROJ / "Assets/Kits/Shaders"

ver = re.search(r"m_EditorVersion: (\S+)", (PROJ / "ProjectSettings/ProjectVersion.txt").read_text()).group(1)
HD = pathlib.Path(rf"C:/Program Files/Unity/Hub/Editor/{ver}/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.high-definition")
src = (HD / "Runtime/Material/Lit/Lit.shader").read_text(encoding="utf-8")

LITDATA = '#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitData.hlsl"'
FORTDATA = '#include "Assets/Kits/Shaders/FortLitData.hlsl"'

VARIANTS = {
    "LitWorldUV": ("Копия HDRP/Lit + кладка по мировым координатам.", ["FORT_WORLDUV"]),
    "LitVertexColor": ("Копия HDRP/Lit + альбедо × цвет вершин.", ["FORT_VERTEXCOLOR"]),
}

for name, (doc, defines) in VARIANTS.items():
    s = src
    assert s.startswith('Shader "HDRP/Lit"')
    s = s.replace('Shader "HDRP/Lit"', f'Shader "Fortress/{name}"', 1)
    # надстройки — только растровый SubShader (первый); трассировка лучей остаётся как в HDRP/Lit
    first, rest = s.split("    SubShader", 1)
    sub1, sub2 = rest.split("    SubShader", 1)
    n = sub1.count(LITDATA)
    assert n >= 8, n
    sub1 = sub1.replace(LITDATA, FORTDATA)
    inject = "    HLSLINCLUDE\n" +"".join(f"    #define {d}\n" for d in defines)
    assert first.count("    HLSLINCLUDE\n") == 1
    first = first.replace("    HLSLINCLUDE\n", inject, 1)
    s = first + "    SubShader" + sub1 + "    SubShader" + sub2
    header = f"// {doc}\n// СГЕНЕРИРОВАНО tools/gen_hdrp_shaders.py из HDRP/Lit Unity {ver} — правки вносить в FortLitData.hlsl.\n"
    (OUT / f"{name}.shader").write_text(header + s, encoding="utf-8", newline="\n")
    print(name, "ok, LitData includes replaced:", n)
