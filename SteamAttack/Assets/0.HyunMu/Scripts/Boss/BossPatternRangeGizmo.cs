using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 각 FSM의 실제 패턴 목록/탐색 거리/공격 거리에서 기즈모를 매번 다시 계산합니다.
/// 기존 FSM이나 공격 코드는 수정하지 않습니다. 거리값을 별도로 저장하지 않습니다.
/// Sources에 BossFSM, 나중에 만들 SnakeFSM 등 MonoBehaviour를 연결하세요.
/// FSM은 직렬화된 patterns(BossAttack[] 또는 List)와 detectRange(float)를 사용해야 합니다.
/// 다른 필드명은 각 Source의 Patterns Field / Detect Range Field에서 지정할 수 있습니다.
/// 중심은 FSM의 rangeOrigin을 자동으로 읽고, 필드가 없거나 비어 있으면 해당 FSM의 Transform입니다.
/// 따라서 두 FSM이 같은 본체에 있어도 뱀의 rangeOrigin이 다르면 별도 중심으로 표시합니다.
/// 새 공격은 기존 BossAttack을 상속합니다. SnakeFSM 클래스 자체는 필요하지 않습니다.
/// 각 FSM 내부는 현재 BossFSM과 같이 목록에서 첫 CanUse 패턴을 고르는 기준입니다.
/// 서로 다른 FSM끼리는 패턴을 합치거나 우선순위를 경쟁시키지 않습니다.
/// 체류 시간, 높이, FSM 상태 전환 및 PrepareAttack 성공 여부는 이 수평 거리 표시와 별도입니다.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Boss/Boss Pattern Range Gizmo")]
public class BossPatternRangeGizmo : MonoBehaviour
{
    [Serializable]
    public class FsmSource
    {
        public bool show = true;
        public string displayName = "FSM";
        [Tooltip("FSM 컴포넌트의 제목줄을 여기에 드래그하세요. BossFSM, SnakeFSM 등 연결 가능.")]
        public MonoBehaviour fsm;
        [Tooltip("기본은 꺼 두세요. 켠 경우에만 Center Override로 표시 중심을 수동 지정합니다.")]
        public bool useCenterOverride = false;
        [Tooltip("Use Center Override를 켠 경우에만 사용합니다. 자동 표시에서는 FSM의 Range Origin을 읽습니다.")]
        public Transform centerOverride;
        [Range(0f, 0.4f)] public float fillOpacity = 0.12f;
        [Range(0f, 360f)] public float labelAngle = 25f;
        public bool showDetectionRange = true;
        [Tooltip("이 FSM에서 사용 가능한 패턴이 없는 거리 구간도 회색으로 표시합니다.")]
        public bool showEmptyRanges = false;

        [Header("FSM 필드 이름 - 현재 코드는 기본값 사용")]
        public string patternsField = "patterns";
        public string detectRangeField = "detectRange";
        [Tooltip("거리 기준점 필드명. SnakeFSM은 rangeOrigin 사용. 이 필드가 없는 BossFSM은 자기 Transform 사용.")]
        public string rangeOriginField = "rangeOrigin";
    }

    [Header("범위를 읽을 FSM 목록 - 항목 추가/삭제 가능")]
    public List<FsmSource> sources = new List<FsmSource>
    {
        new FsmSource { displayName = "거북이", labelAngle = 25f },
        new FsmSource { displayName = "뱀", labelAngle = 155f }
    };

    [Header("표시 설정")]
    public bool showActivationRangeGizmos = true;
    [Tooltip("켜면 이 오브젝트를 선택하지 않아도 표시합니다.")]
    public bool alwaysShow = true;
    public bool showLabels = true;
    public bool showCenterPoints = true;
    [Tooltip("기즈모가 표시될 바닥의 월드 Y 좌표입니다. 실제 거리 판정은 바꾸지 않습니다.")]
    public float rangeGizmoGroundY = 0f;
    public Color closeRangeColor = new Color(1f, 0.35f, 0.1f);
    public Color breathRangeColor = new Color(0.1f, 0.8f, 1f);
    public Color dashRangeColor = new Color(0.8f, 0.3f, 1f);

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (alwaysShow) DrawRanges();
    }

    private void OnDrawGizmosSelected()
    {
        if (!alwaysShow) DrawRanges();
    }

    private void OnValidate()
    {
        UnityEditor.SceneView.RepaintAll();
    }

    private void DrawRanges()
    {
        if (!enabled || !showActivationRangeGizmos || sources == null) return;
        Matrix4x4 previousMatrix = UnityEditor.Handles.matrix;
        Color previousColor = UnityEditor.Handles.color;
        var previousZTest = UnityEditor.Handles.zTest;
        try
        {
            UnityEditor.Handles.matrix = Matrix4x4.identity;
            UnityEditor.Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            for (int i = 0; i < sources.Count; i++)
            {
                FsmSource source = sources[i];
                if (source == null || !source.show) continue;
                if (source.fsm == null)
                {
                    if (showLabels)
                        DrawLabel(transform.position + Vector3.up * (0.3f + i * 0.3f),
                            source.displayName + ": FSM 미연결", Color.gray);
                    continue;
                }
                DrawSource(source);
            }
        }
        finally
        {
            UnityEditor.Handles.matrix = previousMatrix;
            UnityEditor.Handles.color = previousColor;
            UnityEditor.Handles.zTest = previousZTest;
        }
    }

    private void DrawSource(FsmSource source)
    {
        string sourceName = string.IsNullOrEmpty(source.displayName)
            ? source.fsm.GetType().Name : source.displayName;
        List<BossAttack> patterns;
        float detectionRange;
        Transform origin;
        string error;
        if (!TryReadFsm(source, out patterns, out detectionRange, out origin, out error))
        {
            DrawLabel(source.fsm.transform.position, sourceName + ": " + error, Color.yellow);
            return;
        }
        Vector3 center = new Vector3(origin.position.x, rangeGizmoGroundY + 0.03f, origin.position.z);
        if (showCenterPoints)
        {
            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.DrawWireDisc(center, Vector3.up, 0.12f);
            UnityEditor.Handles.DrawLine(center - Vector3.right * 0.18f, center + Vector3.right * 0.18f);
            UnityEditor.Handles.DrawLine(center - Vector3.forward * 0.18f, center + Vector3.forward * 0.18f);
            if (showLabels)
            {
                string mode = source.useCenterOverride && source.centerOverride != null ? " [수동]" : "";
                DrawLabel(center + LabelDirection(source.labelAngle) * 0.25f,
                    $"{sourceName} 중심: {origin.name}{mode}", Color.white);
            }
        }

        // 거리값을 캐시하거나 수동 미리보기로 대체하지 않는다.
        float limit = Mathf.Max(0.1f, detectionRange);
        var edges = new List<float> { 0f, limit };
        foreach (BossAttack pattern in patterns)
        {
            float minimum = Mathf.Max(0f, pattern.minUseDistance);
            float maximum = Mathf.Max(minimum, pattern.maxUseDistance);
            edges.Add(Mathf.Clamp(minimum, 0f, limit));
            edges.Add(Mathf.Clamp(maximum, 0f, limit));
            // 본체와 파동의 도달 거리도 CanUse 경계에 포함한다.
            if (pattern is BossAreaAttack area)
            {
                edges.Add(Mathf.Clamp(Mathf.Max(0.01f, area.radius), 0f, limit));
                if (area.enableWave)
                    edges.Add(Mathf.Clamp(Mathf.Max(0.1f, area.waveMaxRadius), 0f, limit));
            }
        }
        edges.Sort();
        for (int i = edges.Count - 1; i > 0; i--)
            if (edges[i] == edges[i - 1]) edges.RemoveAt(i);

        int start = 0;
        while (start < edges.Count - 1)
        {
            int end = start + 1;
            BossAttack selected = SelectPattern(patterns, (edges[start] + edges[end]) * 0.5f);
            while (end < edges.Count - 1 &&
                SelectPattern(patterns, (edges[end] + edges[end + 1]) * 0.5f) == selected)
                end++;
            if (selected != null || source.showEmptyRanges)
                DrawBand(source, sourceName, patterns, center, edges[start], edges[end], selected);
            start = end;
        }

        if (source.showDetectionRange)
        {
            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.DrawWireDisc(center, Vector3.up, limit);
            if (showLabels)
            {
                string status = source.fsm.isActiveAndEnabled ? "" : " [FSM 비활성]";
                string empty = patterns.Count == 0 ? " / 등록 패턴 없음" : "";
                DrawLabel(center + LabelDirection(source.labelAngle + 60f) * (limit + 0.3f),
                    $"{sourceName} 탐색 거리 {limit:0.##}m{status}{empty}", Color.white);
            }
        }
    }

    private static bool TryReadFsm(FsmSource source, out List<BossAttack> patterns,
        out float detectionRange, out Transform origin, out string error)
    {
        patterns = new List<BossAttack>();
        detectionRange = 0f;
        origin = source.fsm.transform;
        error = "";
        // 읽기만 수행한다. ApplyModifiedProperties 또는 프로퍼티 값 대입은 하지 않는다.
        using (var serialized = new UnityEditor.SerializedObject(source.fsm))
        {
            serialized.UpdateIfRequiredOrScript();
            // SnakeFSM.RangeOrigin과 같은 규칙: 명시된 뿌리가 있으면 사용하고, 없으면 본체.
            // Attack Origin은 피해 범위 기준이며 패턴 발동 거리의 중심으로 사용하지 않는다.
            string originPath = string.IsNullOrEmpty(source.rangeOriginField)
                ? "rangeOrigin" : source.rangeOriginField;
            var originProperty = serialized.FindProperty(originPath);
            if (originProperty != null)
            {
                if (originProperty.propertyType != UnityEditor.SerializedPropertyType.ObjectReference ||
                    (originProperty.objectReferenceValue != null && !(originProperty.objectReferenceValue is Transform)))
                {
                    error = $"'{originPath}'는 Transform 기준점이어야 합니다. Range Origin Field를 확인하세요.";
                    return false;
                }
                Transform configuredOrigin = originProperty.objectReferenceValue as Transform;
                if (configuredOrigin != null) origin = configuredOrigin;
            }
            // 이전에 저장된 Center Override가 있어도 이 옵션을 켜지 않으면 자동 중심을 따른다.
            if (source.useCenterOverride && source.centerOverride != null) origin = source.centerOverride;
            string arrayPath = string.IsNullOrEmpty(source.patternsField) ? "patterns" : source.patternsField;
            string rangePath = string.IsNullOrEmpty(source.detectRangeField) ? "detectRange" : source.detectRangeField;
            var array = serialized.FindProperty(arrayPath);
            var range = serialized.FindProperty(rangePath);
            if (array == null || !array.isArray || array.propertyType == UnityEditor.SerializedPropertyType.String)
            {
                error = $"'{arrayPath}' 패턴 배열/List를 찾지 못했습니다. Patterns Field를 확인하세요.";
                return false;
            }
            if (range == null || (range.propertyType != UnityEditor.SerializedPropertyType.Float &&
                range.propertyType != UnityEditor.SerializedPropertyType.Integer))
            {
                error = $"'{rangePath}' 숫자 필드를 찾지 못했습니다. Detect Range Field를 확인하세요.";
                return false;
            }
            detectionRange = range.propertyType == UnityEditor.SerializedPropertyType.Float
                ? range.floatValue : range.intValue;
            if (float.IsNaN(detectionRange) || float.IsInfinity(detectionRange))
            {
                error = "탐색 거리는 유한한 숫자여야 합니다.";
                return false;
            }
            for (int i = 0; i < array.arraySize; i++)
            {
                var entry = array.GetArrayElementAtIndex(i);
                if (entry.propertyType != UnityEditor.SerializedPropertyType.ObjectReference)
                {
                    error = $"'{arrayPath}'는 BossAttack 컴포넌트 참조 목록이어야 합니다.";
                    return false;
                }
                UnityEngine.Object reference = entry.objectReferenceValue;
                if (reference == null) continue;
                BossAttack pattern = reference as BossAttack;
                if (pattern == null)
                {
                    error = $"'{arrayPath}[{i}]'가 BossAttack을 상속하지 않습니다.";
                    return false;
                }
                if (float.IsNaN(pattern.minUseDistance) || float.IsInfinity(pattern.minUseDistance) ||
                    float.IsNaN(pattern.maxUseDistance))
                {
                    error = pattern.GetType().Name + ": 거리 설정을 확인하세요.";
                    return false;
                }
                if (!patterns.Contains(pattern)) patterns.Add(pattern);
            }
        }
        return true;
    }

    private static BossAttack SelectPattern(List<BossAttack> patterns, float distance)
    {
        // 한 FSM의 목록 안에서만 선택한다. 다른 FSM의 패턴과 합치지 않는다.
        foreach (BossAttack pattern in patterns)
            if (pattern != null && pattern.CanUse(distance)) return pattern;
        return null;
    }

    private void DrawBand(FsmSource source, string sourceName, List<BossAttack> patterns,
        Vector3 center, float inner, float outer, BossAttack pattern)
    {
        string title = pattern != null ? pattern.GetType().Name : "발동 패턴 없음";
        Color color = Color.gray;
        if (pattern is BossAreaAttack area) { title = area.enableWave ? "본체 + 파동 발동" : "근거리"; color = closeRangeColor; }
        else if (pattern is BossBreathAttack) { title = "브레스"; color = breathRangeColor; }
        else if (pattern is BossDashAttack) { title = "돌진"; color = dashRangeColor; }
        else if (pattern is BossArtilleryAttack artillery)
        {
            title = $"포격 후보 · 체류 {Mathf.Max(0f, artillery.requiredStayTime):0.##}초\n시간 미충족 시 다음 패턴 확인";
            color = Color.yellow;
        }
        else if (pattern != null)
        {
            uint hash = 0;
            foreach (char c in title) hash = unchecked(hash * 31u + c);
            color = Color.HSVToRGB((hash % 360u) / 360f, 0.65f, 1f);
        }

        UnityEditor.Handles.color = new Color(color.r, color.g, color.b, source.fillOpacity);
        if (inner <= 0f) UnityEditor.Handles.DrawSolidDisc(center, Vector3.up, outer);
        else DrawRing(center, inner, outer);
        UnityEditor.Handles.color = new Color(color.r, color.g, color.b, 1f);
        if (inner > 0f) UnityEditor.Handles.DrawWireDisc(center, Vector3.up, inner);
        UnityEditor.Handles.DrawWireDisc(center, Vector3.up, outer);
        if (showLabels)
        {
            string lower = SelectPattern(patterns, inner) == pattern ? "≤" : "<";
            string upper = SelectPattern(patterns, outer) == pattern ? "≤" : "<";
            Vector3 position = center + LabelDirection(source.labelAngle) * ((inner + outer) * 0.5f);
            DrawLabel(position, $"{sourceName} / {title}\n{inner:0.##} {lower} 거리 {upper} {outer:0.##}m", color);
        }
    }

    private static Vector3 LabelDirection(float degrees)
    {
        float angle = degrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
    }

    private static void DrawLabel(Vector3 position, string text, Color color)
    {
        var style = new GUIStyle(UnityEditor.EditorStyles.boldLabel);
        style.normal.textColor = color;
        UnityEditor.Handles.Label(position, text, style);
    }

    private static void DrawRing(Vector3 center, float inner, float outer)
    {
        const int segments = 96;
        Vector3[] quad = new Vector3[4];
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            float b = (i + 1) * Mathf.PI * 2f / segments;
            Vector3 from = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 to = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b));
            quad[0] = center + from * inner;
            quad[1] = center + from * outer;
            quad[2] = center + to * outer;
            quad[3] = center + to * inner;
            UnityEditor.Handles.DrawAAConvexPolygon(quad);
        }
    }
#endif
}
