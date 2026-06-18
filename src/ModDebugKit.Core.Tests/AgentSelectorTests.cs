using ModDebugKit.Battles;
using Xunit;

namespace ModDebugKit.Tests
{
    public class AgentSelectorTests
    {
        [Theory]
        [InlineData("all", AgentSelector.All)]
        [InlineData("inf", AgentSelector.Infantry)]
        [InlineData("infantry", AgentSelector.Infantry)]
        [InlineData("ranged", AgentSelector.Ranged)]
        [InlineData("rng", AgentSelector.Ranged)]
        [InlineData("archer", AgentSelector.Ranged)]
        [InlineData("cav", AgentSelector.Cavalry)]
        [InlineData("cavalry", AgentSelector.Cavalry)]
        [InlineData("ha", AgentSelector.HorseArcher)]
        [InlineData("horsearcher", AgentSelector.HorseArcher)]
        [InlineData("  INF  ", AgentSelector.Infantry)]
        public void Parses_known_selectors(string text, AgentSelector expected)
        {
            Assert.True(AgentSelectors.TryParse(text, out var selector));
            Assert.Equal(expected, selector);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("spearmen")]
        public void Rejects_unknown_selectors(string text)
        {
            Assert.False(AgentSelectors.TryParse(text, out _));
        }
    }
}
