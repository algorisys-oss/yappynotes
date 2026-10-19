using YappyNotes.Core;

namespace YappyNotes.Core.Tests;

/// <summary>
/// Keeping a checklist going without typing its Markdown: Enter continues a list,
/// and an item can be added from a plain text field.
/// </summary>
/// <remarks>A <c>|</c> in a case marks the caret.</remarks>
public class TodoListTests
{
    private static string Enter(string marked)
    {
        var caret = marked.IndexOf('|', StringComparison.Ordinal);
        var edit = TodoList.ContinueOnEnter(marked.Remove(caret, 1), caret);

        return edit is null ? "(ordinary Enter)" : edit.Content.Insert(edit.SelectionStart, "|");
    }

    [Fact]
    public void ContinueOnEnter_AtTheEndOfAnItem_StartsANewEmptyItem()
    {
        Assert.Equal("- [ ] milk\n- [ ] |", Enter("- [ ] milk|"));
    }

    /// <summary>A ticked item is done; the next one is not.</summary>
    [Fact]
    public void ContinueOnEnter_AfterATickedItem_StartsAnUntickedOne()
    {
        Assert.Equal("- [x] milk\n- [ ] |", Enter("- [x] milk|"));
    }

    [Fact]
    public void ContinueOnEnter_OnAnIndentedItem_KeepsTheIndent()
    {
        Assert.Equal("- [ ] shop\n  - [ ] milk\n  - [ ] |", Enter("- [ ] shop\n  - [ ] milk|"));
    }

    [Fact]
    public void ContinueOnEnter_InTheMiddleOfAnItem_SplitsItIntoTwo()
    {
        Assert.Equal("- [ ] milk\n- [ ] |bread", Enter("- [ ] milk |bread"));
    }

    /// <summary>
    /// Enter on an empty item is how a list is finished in every editor: the box
    /// goes, and the line is left for ordinary text.
    /// </summary>
    [Fact]
    public void ContinueOnEnter_OnAnEmptyItem_EndsTheList()
    {
        Assert.Equal("- [ ] milk\n|", Enter("- [ ] milk\n- [ ] |"));
    }

    [Fact]
    public void ContinueOnEnter_AtTheEndOfABullet_StartsANewBullet()
    {
        Assert.Equal("- milk\n- |", Enter("- milk|"));
    }

    [Fact]
    public void ContinueOnEnter_OnAnEmptyBullet_EndsTheList()
    {
        Assert.Equal("- milk\n|", Enter("- milk\n- |"));
    }

    [Fact]
    public void ContinueOnEnter_WithTheCaretBeforeTheBox_IsAnOrdinaryEnter()
    {
        Assert.Equal("(ordinary Enter)", Enter("|- [ ] milk"));
    }

    [Theory]
    [InlineData("milk|")]
    [InlineData("# Today|")]
    [InlineData("|")]
    public void ContinueOnEnter_OutsideAList_IsAnOrdinaryEnter(string marked)
    {
        Assert.Equal("(ordinary Enter)", Enter(marked));
    }

    [Fact]
    public void ContinueOnEnter_OnWindowsLineEndings_UsesTheSameLineEnding()
    {
        Assert.Equal("- [ ] tea\r\n- [ ] milk\r\n- [ ] |", Enter("- [ ] tea\r\n- [ ] milk|"));
    }

    [Fact]
    public void Add_ToANoteWithAChecklist_PutsTheItemAfterTheLastOne()
    {
        const string content = "# Shopping\n- [ ] milk\n- [x] bread\n\nthanks";

        Assert.Equal("# Shopping\n- [ ] milk\n- [x] bread\n- [ ] eggs\n\nthanks", TodoList.Add(content, "eggs"));
    }

    [Fact]
    public void Add_AfterAnIndentedItem_MatchesItsIndent()
    {
        Assert.Equal("- [ ] shop\n  - [ ] milk\n  - [ ] eggs", TodoList.Add("- [ ] shop\n  - [ ] milk", "eggs"));
    }

    [Fact]
    public void Add_ToANoteWithoutAChecklist_StartsOneAtTheEnd()
    {
        Assert.Equal("back soon\n- [ ] eggs", TodoList.Add("back soon", "eggs"));
    }

    [Fact]
    public void Add_ToAnEmptyNote_MakesItTheFirstItem()
    {
        Assert.Equal("- [ ] eggs", TodoList.Add(string.Empty, "eggs"));
    }

    [Fact]
    public void Add_ToANoteEndingInALineBreak_DoesNotLeaveABlankLineBefore()
    {
        Assert.Equal("back soon\n- [ ] eggs", TodoList.Add("back soon\n", "eggs"));
    }

    /// <summary>
    /// What was typed is the item's text, not Markdown to interpret, and a line
    /// break in it would split one item into an item and a stray line.
    /// </summary>
    [Fact]
    public void Add_WithSpacesAndALineBreakInTheText_AddsOneTidyItem()
    {
        Assert.Equal("- [ ] eggs and ham", TodoList.Add(string.Empty, "  eggs\nand ham  "));
    }

    [Fact]
    public void Add_WithNothingButSpaces_ChangesNothing()
    {
        Assert.Equal("- [ ] milk", TodoList.Add("- [ ] milk", "   "));
    }

    [Fact]
    public void Add_Result_IsATaskStillToDo()
    {
        var added = NoteMarkdown.Parse(TodoList.Add("- [x] milk", "eggs"))[^1];

        Assert.Equal(MarkdownBlockKind.Task, added.Kind);
        Assert.False(added.IsDone);
    }

    [Fact]
    public void ClearCompleted_WithCodeAfterACompletedTask_PreservesTheWholeFence()
    {
        const string content = "- [x] done\n```bash\n- [x] literal code\n  printf hi\n```\nafter";

        Assert.Equal("```bash\n- [x] literal code\n  printf hi\n```\nafter", TodoList.ClearCompleted(content));
    }
}
