/// <summary>
/// 공용: 피격 경직 단계 (팀 공용 — 모든 파트가 사용)
/// 피해를 줄 때 데미지와 함께 넘겨서, 맞는 쪽이 어떤 반응을 할지 정한다.
/// 데미지 크기와 상관없이 "공격마다" 지정하므로, 성장으로 데미지가 올라도 경직 단계는 바뀌지 않는다.
///
/// 지정 기준 (기획):
///   Light — 일반 공격: 플레이어 약공격, 총, 적 일반 공격, 적 총알
///   Heavy — 강한 공격: 플레이어 강공격, 수류탄, 적 강공격(패링 가능), 소총병 킥, 보스 궁극기
///   None  — 경직 없음: 지속 피해 등
///
/// 사용 예:
///   공격 설정:  public HitStagger stagger = HitStagger.Light;   // 인스펙터에서 드롭다운으로 선택
///   피해 주기:  target.TakeDamage(damage, from, stagger);
///   피해 받기:  슈퍼아머면 None 취급, Light → 움찔(피격 소), Heavy → 크게 비틀거림(피격 대)
/// </summary>
public enum HitStagger
{
    /// <summary>경직 없음</summary>
    None = 0,
    /// <summary>약: 움찔 (피격 소)</summary>
    Light = 1,
    /// <summary>강: 크게 비틀거림 (피격 대)</summary>
    Heavy = 2,
    /// <summary>넘어짐: 쓰러졌다가 일어남 (패링 불가 강공격, 보스 궁극기 등)</summary>
    Knockdown = 3,
}