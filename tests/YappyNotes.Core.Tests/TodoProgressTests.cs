using YappyNotes.Core;

namespace YappyNotes.Core.Tests;

/// <summary>How far through a checklist a note is, and clearing what is done.</summary>
public class TodoProgressTests
{
    [Fact]
    public void Progress_OfAChecklist_CountsTheTickedAndAllItems()
    {
        var progress = TodoList.Progress("# Shop\n- [x] bread\n- [ ] milk\n  - [X] oat\n- plain bullet");

        Assert.Equal(new TodoProgress(Done: 2, Total: 3), progress);
    }

    [Fact]
    public void Progress_OfANoteWithoutTodos_IsNothingOfNothing()
    {
        Assert.Equal(new TodoProgress(0, 0), TodoList.Progress("back soon"));
    }

    [Fact]
    public void ClearCompleted_RemovesEachTickedItemsLine()
    {
        Assert.Equal("# Shop\n- [ ] milk\nthanks", TodoList.ClearCompleted("# Shop\n- [x] bread\n- [ ] milk\n- [x] tea\nthanks"));
    }

    [Fact]
    public void ClearCompleted_OnTheLastLine_LeavesNoTrailingLineBreak()
    {
        Assert.Equal("- [ ] milk", TodoList.ClearCompleted("- [ ] milk\n- [x] bread"));
    }

    [Fact]
    public void ClearCompleted_WithEverythingDone_LeavesTheRestOfTheNote()
    {
        Assert.Equal("# Shop", TodoList.ClearCompleted("# Shop\n- [x] bread\n- [x] milk"));
    }

    /// <summary>
    /// A ticked item with an unticked one under it is a heading for work still to
    /// do. Removing it would leave the child hanging under whatever came before.
    /// </summary>
    [Fact]
    public void ClearCompleted_OnATickedItemWithUntickedItemsUnderIt_KeepsIt()
    {
        const string content = "- [x] groceries\n  - [ ] milk\n  - [x] bread";

        Assert.Equal("- [x] groceries\n  - [ ] milk", TodoList.ClearCompleted(content));
    }

    [Fact]
    public void ClearCompleted_OnATickedItemWhoseItemsAreAllTicked_RemovesThemAll()
    {
        Assert.Equal("- [ ] tea", TodoList.ClearCompleted("- [x] groceries\n  - [x] milk\n- [ ] tea"));
    }

    [Fact]
    public void ClearCompleted_OnWindowsLineEndings_KeepsThem()
    {
        Assert.Equal("- [ ] milk\r\nthanks", TodoList.ClearCompleted("- [x] bread\r\n- [ ] milk\r\nthanks"));
    }

    [Fact]
    public void ClearCompleted_WithNothingTicked_ChangesNothing()
    {
        Assert.Equal("- [ ] milk\n- [ ] bread", TodoList.ClearCompleted("- [ ] milk\n- [ ] bread"));
    }

    [Fact]
    public void TodoList_WithImageBetweenTasks_PreservesReference()
    {
        const string image = "![café](file:///tmp/caf%C3%A9%20da%20manh%C3%A3.webp)";
        var content = $"- [x] bread\n{image}\n- [ ] milk";
        var milk = Assert.Single(NoteMarkdown.Parse(content), block => block.Kind == MarkdownBlockKind.Task && !block.IsDone);

        var ticked = NoteMarkdown.ToggleTask(content, milk.CheckMarkIndex);
        Assert.Equal($"- [x] bread\n{image}\n- [x] milk", ticked);

        var cleared = TodoList.ClearCompleted(content);
        Assert.Equal($"{image}\n- [ ] milk", cleared);

        var added = TodoList.Add(content, "tea");
        Assert.Equal($"- [x] bread\n{image}\n- [ ] milk\n- [ ] tea", added);

        Assert.All([ticked, cleared, added], value =>
            Assert.Single(NoteMarkdown.Parse(value), block => block.Kind == MarkdownBlockKind.Image && block.ImageUri is not null));
    }
}
