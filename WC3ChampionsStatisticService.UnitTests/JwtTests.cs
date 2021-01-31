using NUnit.Framework;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class JwtTests
    {
        private string _jwt = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJCYXR0bGVUYWciOiJtb2Rtb3RvIzI4MDkiLCJJc0FkbWluIjoiVHJ1ZSIsIk5hbWUiOiJtb2Rtb3RvIn0.Y4xe1wqRceSdJW2evar5LFVsWfixZUUQtWWckehnkNwVpGiNIzQb90GP30fzOFt9GKUXO7ADNuy4ss8tTNxlvSiYmkT9Ulx1-ve64WO8SYJUBwFVqPorBrunz628tFyf4t1YMt_q_lfbVuQc1WdJiNVqFy1FNzkWENW-GsZbJB-shrCIVj9qp_MtP7MC0Bata7XCjTszlZnVAJUh7-iBPlUhSg8405U5aHkGpPzjLRgQtlGm6s8F1lYOyIzT-rCCvAI_dVI3F4ee6cjS0MbY9m8KPjloOx2NJGKvbwE0dAKBszKbQ7Ic3zr6yCvj-FBt82VmAaDan7pzXJLyZcSnFbikhsKSjLzcAXw1fP_I-FhEIvS-9vysWmXx9uNF91cDlXvdZZo57gV7o6vS4CgXscvpwiPQ9KnKsQA3Ezn61snZoXjGKspiTI_yblC4zLPHm-s40RmPOI_9TwxaiOurl6GjZk1uNY5dm7cGQjh4QWbha8CkllAmgknKOfQw9Mj7TvEKukkFetKF96jOjnqBFQUVXM8YL8K9rzATEy45vkPbfTs7MP9dHUVyEUYfD-HoYMpexEkPRwpCsLty2VfDmIV9Jkj3yOh3ybeKgv7N3Dh8ROx2lxSnqZhyc5HfE_AsnjaLTq2SvEqJ4ndYtYH9rVIARx0p_gPBZF9kAl-Nb2M";

        [Test]
        public void GetToken()
        {
            var w3CAuthenticationService = new W3CAuthenticationService();
            var userByToken1 = w3CAuthenticationService.GetUserByToken(_jwt);

            Assert.AreEqual("modmoto#2809", userByToken1.BattleTag);
        }
    }
}