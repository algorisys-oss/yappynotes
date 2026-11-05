using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.Time.Testing;

namespace YappyNotes.App.Tests;

public class LinuxWindowActivationGroupTests
{
    [AvaloniaFact]
    public void LinuxWindowActivationGroup_WhenManagerActivated_RaisesNotesBeforeManager()
    {
        var manager = new Window();
        var first = new Window();
        var second = new Window();
        manager.Show();
        first.Show();
        second.Show();
        var raised = new List<Window>();
        var group = new LinuxWindowActivationGroup(raised.Add);
        group.Register(manager);
        group.Register(first);
        group.Register(second);

        group.BringForward(manager);

        Assert.Equal([first, second, manager], raised);
    }

    [AvaloniaFact]
    public void LinuxWindowActivationGroup_WhenManagerClosed_RaisesOnlyOpenNotes()
    {
        var manager = new Window();
        var first = new Window();
        var second = new Window();
        manager.Show();
        first.Show();
        second.Show();
        var raised = new List<Window>();
        var group = new LinuxWindowActivationGroup(raised.Add);
        group.Register(manager);
        group.Register(first);
        group.Register(second);
        manager.Close();

        group.BringForward(first);

        Assert.Equal([second, first], raised);
    }

    [AvaloniaFact]
    public void LinuxWindowActivationGroup_WhenRaiseTriggersActivation_DoesNotRepeatTheGroup()
    {
        var first = new Window();
        var second = new Window();
        first.Show();
        second.Show();
        var raised = new List<Window>();
        LinuxWindowActivationGroup? group = null;
        group = new LinuxWindowActivationGroup(window =>
        {
            raised.Add(window);
            group!.BringForward(window);
        });
        group.Register(first);
        group.Register(second);

        group.BringForward(first);

        Assert.Equal([second, first], raised);
    }

    [AvaloniaFact]
    public void LinuxWindowActivationGroup_AfterAnIgnoredActivationExpires_AllowsTheNextSwitch()
    {
        var first = new Window();
        var second = new Window();
        first.Show();
        second.Show();
        var clock = new FakeTimeProvider();
        var raised = new List<Window>();
        var group = new LinuxWindowActivationGroup(raised.Add, clock);
        group.Register(first);
        group.Register(second);
        group.BringForward(first);
        clock.Advance(TimeSpan.FromSeconds(1));

        group.BringForward(second);

        Assert.Equal([second, first, first, second], raised);
    }
}
