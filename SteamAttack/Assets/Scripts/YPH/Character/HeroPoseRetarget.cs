using UnityEngine;

namespace YPH
{
    /// <summary>기존 Generic 동작을 두 Avatar의 체격에 맞춰 Hero에 전달합니다.</summary>
    [RequireComponent(typeof(Animator))]
    [DefaultExecutionOrder(900)]
    public sealed class HeroPoseRetarget : MonoBehaviour
    {
        [Tooltip("기존 Generic 애니메이션이 재생되는 골격의 루트")]
        [SerializeField] private Transform sourceRoot;
        [Tooltip("Hero Animator와 Avatar가 있는 골격의 루트")]
        [SerializeField] private Transform targetRoot;
        [Tooltip("원본 골격을 T 자세로 보정해서 만든 개인용 Humanoid Avatar")]
        [SerializeField] private Avatar sourceAvatar;
        [SerializeField] private GameObject drawnSword;
        [SerializeField] private Renderer sheathedSword;

        private HumanPoseHandler sourcePose;
        private HumanPoseHandler targetPose;
        private HumanPose pose;

        public void ApplyPose()
        {
            if (sourcePose == null)
            {
                var animator = targetRoot ? targetRoot.GetComponent<Animator>() : null;
                if (!sourceRoot || !sourceAvatar || !sourceAvatar.isValid || !sourceAvatar.isHuman ||
                    !animator || !animator.avatar || !animator.avatar.isValid || !animator.avatar.isHuman)
                {
                    Debug.LogError("Hero 자세 전달에는 원본과 Hero의 유효한 Humanoid Avatar가 필요합니다.", this);
                    enabled = false;
                    return;
                }
                sourcePose = new HumanPoseHandler(sourceAvatar, sourceRoot);
                targetPose = new HumanPoseHandler(animator.avatar, targetRoot);
            }

            // Avatar의 관절 축과 humanScale로 변환해야 서로 다른 체격에도 같은 동작이 된다.
            sourcePose.GetHumanPose(ref pose);
            targetPose.SetHumanPose(ref pose);
            if (sheathedSword && drawnSword)
                sheathedSword.enabled = !drawnSword.activeInHierarchy;
        }

        private void LateUpdate() => ApplyPose();

        private void OnDisable()
        {
            sourcePose?.Dispose();
            targetPose?.Dispose();
            sourcePose = null;
            targetPose = null;
        }
    }
}
