using UnityEngine;

namespace YPH
{
    /// <summary>
    /// 새 연출의 오방색 기본값입니다. 출처는 NMJ HyeonmuWorldBuilder의 상수와 통합 기획서 0-4입니다.
    /// 팔레트가 바뀌면 이 파일을 고치고, 이미 저장된 프리팹·재질의 색도 함께 맞춥니다.
    /// </summary>
    public static class ObangColors
    {
        /// <summary>청: 시간·되감기 전용입니다. 이번 조준·공격 이펙트에는 쓰지 않습니다.</summary>
        public static readonly Color Cheong = new Color(0.12f, 0.46f, 0.70f);
        /// <summary>적: 열기·저항입니다. 조준 줄에만 쓰고 적 피격 불꽃에는 쓰지 않습니다.</summary>
        public static readonly Color Jeok = new Color(0.82f, 0.18f, 0.10f);
        /// <summary>황: 어명·생명의 핵이며 공격 연출에서는 섬광·불똥의 기본색입니다.</summary>
        public static readonly Color Hwang = new Color(0.94f, 0.73f, 0.16f);
        /// <summary>백: 한지·증기의 흰색입니다. 연기·궤적의 기본색입니다.</summary>
        public static readonly Color Baek = new Color(0.96f, 0.95f, 0.92f);
        /// <summary>흑: 현무·동결의 먹색입니다. 비네트와 먼지의 기본색입니다.</summary>
        public static readonly Color Heuk = new Color(0.06f, 0.06f, 0.07f);
    }
}
