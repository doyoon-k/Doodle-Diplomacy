namespace DoodleDiplomacy.Core
{
    /// <summary>
    /// 라운드 판정에서 각 축(협력-지배, 효율-공감)별 만족도 카테고리.
    /// AI 파이프라인 → ScoreManager로 전달되는 값.
    /// </summary>
    public enum SatisfactionLevel
    {
        VeryDissatisfied = -2,
        Dissatisfied     = -1,
        Neutral          =  0,
        Satisfied        =  1,
        VerySatisfied    =  2
    }
}
