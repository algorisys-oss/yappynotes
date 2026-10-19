using YappyNotes.Core;

namespace YappyNotes.Core.Tests;

/// <summary>A note's to-dos read out of it, and ticked from somewhere else.</summary>
public class TodoItemsTests
{
    [Fact]
    public void OpenItems_OfAChecklist_AreTheUntickedItemsInOrder()
    {
        var items = TodoList.OpenItems("- [ ] milk\n- [x] bread\n  - [ ] oat\nplain");

        Assert.Equal(["milk", "oat"], items.Select(item => item.Text));
    }

    [Fact]
    public void OpenItems_WithABoxInsideCode_DoesNotCreateATodo()
    {
        var items = TodoList.OpenItems("- [ ] outside\n```\n- [ ] inside code\n```\n- [x] done");

        Assert.Equal("outside", Assert.Single(items).Text);
    }

    /// <summary>A list elsewhere shows words, not the Markdown around them.</summary>
    [Fact]
    public void OpenItems_WithFormattingInTheText_GiveThePlainWords()
    {
        var item = Assert.Single(TodoList.OpenItems("- [ ] buy **cold** milk at [the shop](https://example.com)"));

        Assert.Equal("buy cold milk at the shop", item.Text);
    }

    [Fact]
    public void OpenItems_KeepEachItemsIndent()
    {
        Assert.Equal([0, 1], TodoList.OpenItems("- [ ] shop\n  - [ ] milk").Select(item => item.Level));
    }

    [Fact]
    public void OpenItems_KnowWhereEachCheckMarkIs()
    {
        const string content = "# Shop\n- [ ] milk";

        var item = Assert.Single(TodoList.OpenItems(content));

        Assert.Equal(' ', content[item.CheckMarkIndex]);
        Assert.Equal('[', content[item.CheckMarkIndex - 1]);
    }

    [Fact]
    public void Tick_AnItemThatIsStillThere_TicksIt()
    {
        const string content = "- [ ] milk\n- [ ] bread";
        var bread = TodoList.OpenItems(content)[1];

        Assert.Equal("- [ ] milk\n- [x] bread", TodoList.Tick(content, bread));
    }

    /// <summary>
    /// The list the item came from can be older than the note. If the note has been
    /// edited since, another item can sit where this one's box was - and ticking
    /// that one instead would be quietly wrong.
    /// </summary>
    [Fact]
    public void Tick_WhereADifferentItemNowSits_ChangesNothing()
    {
        var milk = TodoList.OpenItems("- [ ] milk")[0];
        const string since = "- [ ] eggs\n- [ ] milk";

        Assert.Equal(since, TodoList.Tick(since, milk));
    }

    [Fact]
    public void Tick_AnItemAlreadyTickedSince_LeavesItTicked()
    {
        var milk = TodoList.OpenItems("- [ ] milk")[0];

        Assert.Equal("- [x] milk", TodoList.Tick("- [x] milk", milk));
    }
}
