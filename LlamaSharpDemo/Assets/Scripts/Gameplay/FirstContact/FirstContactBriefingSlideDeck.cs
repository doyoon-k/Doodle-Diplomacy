using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public enum FirstContactBriefingSlideId
    {
        Technical,
        ObjectSignal,
        Banana,
        Database,
        PresidentTask,
        FoodCluster,
        UfoEvidence,
        Categories
    }

    [CreateAssetMenu(
        fileName = "FirstContactBriefingSlides",
        menuName = "DoodleDiplomacy/First Contact/Briefing Slide Deck")]
    public sealed class FirstContactBriefingSlideDeck : ScriptableObject
    {
        [SerializeField] private Texture2D technical;
        [SerializeField] private Texture2D objectSignal;
        [SerializeField] private Texture2D banana;
        [SerializeField] private Texture2D database;
        [SerializeField] private Texture2D presidentTask;
        [SerializeField] private Texture2D foodCluster;
        [SerializeField] private Texture2D ufoEvidence;
        [SerializeField] private Texture2D categories;

        public Texture2D GetSlide(FirstContactBriefingSlideId slideId)
        {
            return slideId switch
            {
                FirstContactBriefingSlideId.Technical => technical,
                FirstContactBriefingSlideId.ObjectSignal => objectSignal,
                FirstContactBriefingSlideId.Banana => banana,
                FirstContactBriefingSlideId.Database => database,
                FirstContactBriefingSlideId.PresidentTask => presidentTask,
                FirstContactBriefingSlideId.FoodCluster => foodCluster,
                FirstContactBriefingSlideId.UfoEvidence => ufoEvidence,
                FirstContactBriefingSlideId.Categories => categories,
                _ => null
            };
        }

        public static string GetDisplayName(FirstContactBriefingSlideId slideId)
        {
            return slideId switch
            {
                FirstContactBriefingSlideId.Technical => "기술 폭주",
                FirstContactBriefingSlideId.ObjectSignal => "1. 사물과 반응 신호",
                FirstContactBriefingSlideId.Banana => "2. 바나나 예시",
                FirstContactBriefingSlideId.Database => "3. 데이터베이스의 한계",
                FirstContactBriefingSlideId.PresidentTask => "4. 대통령의 역할",
                FirstContactBriefingSlideId.FoodCluster => "5. 음식 반응 패턴",
                FirstContactBriefingSlideId.UfoEvidence => "6. 외계인의 기존 행적",
                FirstContactBriefingSlideId.Categories => "7. 네 가지 분류",
                _ => slideId.ToString()
            };
        }

        public static string GetSerializedFieldName(FirstContactBriefingSlideId slideId)
        {
            return slideId switch
            {
                FirstContactBriefingSlideId.Technical => nameof(technical),
                FirstContactBriefingSlideId.ObjectSignal => nameof(objectSignal),
                FirstContactBriefingSlideId.Banana => nameof(banana),
                FirstContactBriefingSlideId.Database => nameof(database),
                FirstContactBriefingSlideId.PresidentTask => nameof(presidentTask),
                FirstContactBriefingSlideId.FoodCluster => nameof(foodCluster),
                FirstContactBriefingSlideId.UfoEvidence => nameof(ufoEvidence),
                FirstContactBriefingSlideId.Categories => nameof(categories),
                _ => string.Empty
            };
        }
    }
}
