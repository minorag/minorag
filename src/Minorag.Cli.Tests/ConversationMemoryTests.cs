using Minorag.Cli.Services.Chat;
using Minorag.Cli.Tests.TestInfrastructure;

namespace Minorag.Cli.Tests;

public class ConversationMemoryTests
{
    private static ConversationMemory CreateMemory(int maxTurns = 8)
    {
        var embedding = new FakeEmbeddingProvider
        {
            EmbeddingToReturn = [1f]
        };

        return new ConversationMemory(embedding, maxTurns);
    }

    [Fact]
    public void AddTurn_StoresTurns_AndReturnsMostRecentFirst()
    {
        // Arrange
        var memory = CreateMemory(maxTurns: 8);

        // Act
        memory.AddTurn("q1", "a1");
        memory.AddTurn("q2", "a2");
        memory.AddTurn("q3", "a3");

        var recent = memory.GetRecent(10); // ask for more than we have

        // Assert
        Assert.Equal(3, recent.Count);

        Assert.Equal("q3", recent[0].Question);
        Assert.Equal("a3", recent[0].Answer);

        Assert.Equal("q2", recent[1].Question);
        Assert.Equal("a2", recent[1].Answer);

        Assert.Equal("q1", recent[2].Question);
        Assert.Equal("a1", recent[2].Answer);
    }

    [Fact]
    public void GetRecent_LimitsNumberOfTurns()
    {
        // Arrange
        var memory = CreateMemory(maxTurns: 8);

        memory.AddTurn("q1", "a1");
        memory.AddTurn("q2", "a2");
        memory.AddTurn("q3", "a3");

        // Act
        var recent = memory.GetRecent(2);

        // Assert
        Assert.Equal(2, recent.Count);
        Assert.Equal("q3", recent[0].Question);
        Assert.Equal("q2", recent[1].Question);
    }

    [Fact]
    public void AddTurn_WhenExceedingMaxTurns_EvictsOldest()
    {
        // Arrange
        var memory = CreateMemory(maxTurns: 3);

        // Act
        memory.AddTurn("q1", "a1");
        memory.AddTurn("q2", "a2");
        memory.AddTurn("q3", "a3");
        memory.AddTurn("q4", "a4"); // should evict q1/a1

        var recent = memory.GetRecent(10);

        // Assert
        Assert.Equal(3, recent.Count);

        // Newest first
        Assert.Equal("q4", recent[0].Question);
        Assert.Equal("q3", recent[1].Question);
        Assert.Equal("q2", recent[2].Question);

        Assert.DoesNotContain(recent, t => t.Question == "q1" && t.Answer == "a1");
    }

    [Fact]
    public void Clear_RemovesAllHistory()
    {
        // Arrange
        var memory = CreateMemory(maxTurns: 8);

        memory.AddTurn("q1", "a1");
        memory.AddTurn("q2", "a2");

        // Act
        memory.Clear();
        var recent = memory.GetRecent(10);

        // Assert
        Assert.Empty(recent);
    }
}