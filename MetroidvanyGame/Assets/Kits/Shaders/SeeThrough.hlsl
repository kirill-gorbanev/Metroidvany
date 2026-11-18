#ifndef FORTRESS_SEETHROUGH_INCLUDED
#define FORTRESS_SEETHROUGH_INCLUDED

// Разрез, когда камеру заслоняет (стена, потолок, камера в толще стены или под землёй) — «кукольный домик»:
//  1) вне комнаты героя (прямоугольник между ближними стенами, от низа его пола до верха его потолка) не рисуется ничего —
//     куски других мест не видны, за стеной не подглядеть;
//  2) внутри комнаты всё, что ближе героя к камере, в высоте его этажа убрано; край разреза — сеткой (дизеринг 4×4).
// Значения задаёт SeeThroughCamera перед кадром игровой камеры; для остальных камер C.x = 0 — ничего не режется.
// _SeeThroughA: x — глубина разреза от камеры (м), y — ширина сетчатого края (м), z, w — низ и верх разреза (мир)
// _SeeThroughB: комната по x, z: (xmin, zmin, xmax, zmax), мир
// _SeeThroughC: x — сила 0..1, y, z — низ и верх комнаты (мир), w — ширина растворения у её границы (м)
// (коробка B/C — внутренние лица стен, пола, потолка плюс w: от них наружу всё растворяется сеткой)
float4 _SeeThroughA;
float4 _SeeThroughB;
float4 _SeeThroughC;

static const float kSeeThroughBayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

void SeeThroughClip(float4 positionCS)
{
    if (_SeeThroughC.x <= 0.001)
        return;
    float3 ws = ComputeWorldSpacePosition(positionCS.xy / _ScaledScreenParams.xy, positionCS.z, UNITY_MATRIX_I_VP);
    uint2 p = (uint2)positionCS.xy & 3;
    float th = (kSeeThroughBayer[p.y * 4 + p.x] + 0.5) / 16.0;
    // 1) у границы комнаты героя и вне её: от внутренних лиц стен наружу — растворение сеткой (толща стен, наружные
    //    углы и плоскости уходят), дальше — ничего. d — насколько точка вне коробки (внутри — отрицательно)
    float dx = max(_SeeThroughB.x - ws.x, ws.x - _SeeThroughB.z);
    float dz = max(_SeeThroughB.y - ws.z, ws.z - _SeeThroughB.w);
    float dy = max(_SeeThroughC.y - ws.y, ws.y - _SeeThroughC.z);
    float d = max(dx, max(dz, dy));
    float kBox = saturate((d + _SeeThroughC.w) / max(_SeeThroughC.w, 1e-3)) * _SeeThroughC.x;
    clip(th - kBox);
    // 2) ближе героя — в высоте его этажа
    float eye = LinearEyeDepth(positionCS.z, _ZBufferParams);
    float front = _SeeThroughA.x - eye;
    if (front <= 0.0 || ws.y < _SeeThroughA.z || ws.y > _SeeThroughA.w)
        return;
    clip(th - saturate(front / max(_SeeThroughA.y, 1e-3)));
}

#endif
