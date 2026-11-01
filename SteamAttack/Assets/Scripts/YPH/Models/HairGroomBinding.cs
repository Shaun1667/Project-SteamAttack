using UnityEngine;

namespace YPH
{
    /// <summary>
    /// 원본 머리 렌더러 아래에 둔 보강 프리팹을 기존 뼈와 연결한다.
    /// 본체 Mesh를 교체하지 않고 보강 렌더러를 사용하며, 끄면 원본 표시를 복원한다.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class HairGroomBinding : MonoBehaviour
    {
        [Tooltip("보강본의 기준인 원본 Mesh입니다. 부모의 Mesh가 다르면 잘못된 리깅을 막기 위해 적용하지 않습니다.")]
        [SerializeField] private Mesh _originalMesh;

        [Tooltip("홈을 보강한 본체의 SkinnedMeshRenderer입니다. 비워 두면 원본 표시를 유지합니다.")]
        [SerializeField] private SkinnedMeshRenderer _baseRenderer;

        [Tooltip("잔머리 Hair Cards의 SkinnedMeshRenderer입니다. 비워 두면 원본 표시를 유지합니다.")]
        [SerializeField] private SkinnedMeshRenderer _cardsRenderer;

        // 편집기 재컴파일와 Play 전환 후에도 원본의 활성 상태를 복원할 수 있게 저장한다.
        [SerializeField, HideInInspector] private SkinnedMeshRenderer _sourceRenderer;
        [SerializeField, HideInInspector] private bool _sourceWasEnabled;
        [SerializeField, HideInInspector] private bool _isApplied;

        /// <summary>활성화와 도메인 재로드 시 기존 리깅에 다시 연결한다.</summary>
        private void OnEnable()
        {
            Apply();
        }

        /// <summary>보강 프리팹을 끄면 자신이 감췄던 원본만 다시 표시한다.</summary>
        private void OnDisable()
        {
            Restore();
        }

        /// <summary>다른 모델로 옮길 때 이전 모델의 표시를 먼저 복원한다.</summary>
        private void OnTransformParentChanged()
        {
            Restore();
            if (isActiveAndEnabled)
            {
                Apply();
            }
        }

        /// <summary>
        /// 부모에서 원본 렌더러를 찾아 뼈·재질·그림자 설정을 복사한다.
        /// 반복 호출해도 원본의 활성 상태를 덮어쓰지 않으며 매 프레임 실행하지 않는다.
        /// </summary>
        public bool Apply()
        {
            SkinnedMeshRenderer source = transform.parent != null
                ? transform.parent.GetComponentInParent<SkinnedMeshRenderer>() : null;
            if (source == null || source.sharedMesh != _originalMesh || _baseRenderer == null
                || _cardsRenderer == null || _baseRenderer.sharedMesh == null || _cardsRenderer.sharedMesh == null
                || source.bones.Length != _baseRenderer.sharedMesh.bindposes.Length
                || source.bones.Length != _cardsRenderer.sharedMesh.bindposes.Length)
            {
                Restore();
                return false;
            }

            if (_isApplied && _sourceRenderer != source)
            {
                Restore();
            }
            if (!_isApplied)
            {
                _sourceRenderer = source;
                _sourceWasEnabled = source.enabled;
            }

            // Mesh와 Material 자산은 수정하지 않는다. 연결 값만 각 렌더러에 설정한다.
            BindRenderer(_baseRenderer, source);
            BindRenderer(_cardsRenderer, source);
            _baseRenderer.sharedMaterials = source.sharedMaterials;
            _baseRenderer.enabled = _sourceWasEnabled;
            _cardsRenderer.enabled = _sourceWasEnabled;
            source.enabled = false;
            _isApplied = true;
            // 뼈 연결 전에는 Renderer의 바운드가 Hips 좌표로 남아 있다.
            // 연결이 끝난 월드 바운드로 LOD 중심과 크기를 다시 계산한다.
            LODGroup lod = GetComponent<LODGroup>();
            if (lod != null)
            {
                lod.RecalculateBounds();
                // 실제 게임의 culling 바운드는 갓까지 포함해 넓어질 수 있다.
                // LOD 크기는 이 프리팹의 로컬 공간에 있는 카드 Mesh로 고정한다.
                Vector3 size = _cardsRenderer.sharedMesh.bounds.size;
                lod.size = Mathf.Max(size.x, size.y, size.z);
            }
            return true;
        }

        /// <summary>이 컴포넌트가 바꾼 렌더러의 표시 상태만 원래대로 되돌린다.</summary>
        private void Restore()
        {
            if (_isApplied && _sourceRenderer != null)
            {
                _sourceRenderer.enabled = _sourceWasEnabled;
            }
            if (_baseRenderer != null)
            {
                _baseRenderer.enabled = false;
            }
            if (_cardsRenderer != null)
            {
                _cardsRenderer.enabled = false;
            }
            _sourceRenderer = null;
            _isApplied = false;
        }

        /// <summary>같은 Bind Pose를 쓰는 렌더러에 뼈와 원본의 렌더 설정을 연결한다.</summary>
        private static void BindRenderer(SkinnedMeshRenderer target, SkinnedMeshRenderer source)
        {
            target.gameObject.layer = source.gameObject.layer;
            target.bones = source.bones;
            target.rootBone = source.rootBone;
            target.localBounds = source.localBounds;
            target.quality = source.quality;
            target.renderingLayerMask = source.renderingLayerMask;
            target.shadowCastingMode = source.shadowCastingMode;
            target.receiveShadows = source.receiveShadows;
            target.lightProbeUsage = source.lightProbeUsage;
            target.reflectionProbeUsage = source.reflectionProbeUsage;
            target.probeAnchor = source.probeAnchor;
            target.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
            target.skinnedMotionVectors = source.skinnedMotionVectors;
            // 편집기의 정지 프레임에서도 새 스키닝 버퍼가 갱신되게 한다.
            // 실제 게임에서는 원본 설정을 따라 화면 밖의 불필요한 스키닝을 막는다.
            target.updateWhenOffscreen = !Application.isPlaying || source.updateWhenOffscreen;
        }
    }
}
