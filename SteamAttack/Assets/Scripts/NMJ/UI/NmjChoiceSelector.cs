using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NMJ
{
    /// <summary>
    /// ◀ 값 ▶ 고르기 칸. 고른 상태에서 ←/→ 키로 바꾸고, 마우스는 양쪽 화살표를 누른다.
    /// 환경 설정의 해상도·화면 모드에 쓴다 (SettingsMenu가 코드로 만든다).
    /// </summary>
    public class NmjChoiceSelector : Selectable
    {
        /// <summary>-1 = 왼쪽, +1 = 오른쪽</summary>
        public Action<int> OnStep;

        public override void OnMove(AxisEventData eventData)
        {
            if (eventData.moveDir == MoveDirection.Left) { OnStep?.Invoke(-1); eventData.Use(); return; }
            if (eventData.moveDir == MoveDirection.Right) { OnStep?.Invoke(1); eventData.Use(); return; }
            base.OnMove(eventData);
        }
    }
}
