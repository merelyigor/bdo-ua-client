using System.Windows.Forms;

namespace BdoClient.Tests;

public class MainFormClosePolicyTests
{
    [Fact]
    public void UserClosing_Idle_NormalX_HidesToTray()
    {
        var action = MainForm.EvaluateCloseAction(
            CloseReason.UserClosing,
            explicitExitRequested: false,
            exitAfterOperation: false,
            operationInProgress: false);

        Assert.Equal(MainForm.MainFormCloseAction.HideToTray, action);
    }

    [Fact]
    public void UserClosing_Active_NormalX_HidesToTray()
    {
        var action = MainForm.EvaluateCloseAction(
            CloseReason.UserClosing,
            explicitExitRequested: false,
            exitAfterOperation: false,
            operationInProgress: true);

        Assert.Equal(MainForm.MainFormCloseAction.HideToTray, action);
    }

    [Fact]
    public void UserClosing_Idle_ExplicitExit_ExitsNow()
    {
        var action = MainForm.EvaluateCloseAction(
            CloseReason.UserClosing,
            explicitExitRequested: true,
            exitAfterOperation: false,
            operationInProgress: false);

        Assert.Equal(MainForm.MainFormCloseAction.ExitNow, action);
    }

    [Fact]
    public void UserClosing_Active_ExplicitExit_Defers()
    {
        var action = MainForm.EvaluateCloseAction(
            CloseReason.UserClosing,
            explicitExitRequested: true,
            exitAfterOperation: false,
            operationInProgress: true);

        Assert.Equal(MainForm.MainFormCloseAction.DeferUntilOperationCompletes, action);
    }

    [Fact]
    public void UserClosing_Idle_ExitAfterOperation_ExitsNow()
    {
        var action = MainForm.EvaluateCloseAction(
            CloseReason.UserClosing,
            explicitExitRequested: false,
            exitAfterOperation: true,
            operationInProgress: false);

        Assert.Equal(MainForm.MainFormCloseAction.ExitNow, action);
    }

    [Fact]
    public void UserClosing_Active_ExitAfterOperation_Defers()
    {
        var action = MainForm.EvaluateCloseAction(
            CloseReason.UserClosing,
            explicitExitRequested: false,
            exitAfterOperation: true,
            operationInProgress: true);

        Assert.Equal(MainForm.MainFormCloseAction.DeferUntilOperationCompletes, action);
    }

    [Fact]
    public void WindowsShutDown_Idle_ExitsNow()
    {
        var action = MainForm.EvaluateCloseAction(
            CloseReason.WindowsShutDown,
            explicitExitRequested: false,
            exitAfterOperation: false,
            operationInProgress: false);

        Assert.Equal(MainForm.MainFormCloseAction.ExitNow, action);
    }

    [Fact]
    public void WindowsShutDown_Active_Defers()
    {
        var action = MainForm.EvaluateCloseAction(
            CloseReason.WindowsShutDown,
            explicitExitRequested: false,
            exitAfterOperation: false,
            operationInProgress: true);

        Assert.Equal(MainForm.MainFormCloseAction.DeferUntilOperationCompletes, action);
    }

    [Fact]
    public void ApplicationExitCall_Active_Defers()
    {
        var action = MainForm.EvaluateCloseAction(
            CloseReason.ApplicationExitCall,
            explicitExitRequested: false,
            exitAfterOperation: false,
            operationInProgress: true);

        Assert.Equal(MainForm.MainFormCloseAction.DeferUntilOperationCompletes, action);
    }

    [Fact]
    public void TaskManagerClose_Active_Defers()
    {
        var action = MainForm.EvaluateCloseAction(
            CloseReason.TaskManagerClosing,
            explicitExitRequested: false,
            exitAfterOperation: false,
            operationInProgress: true);

        Assert.Equal(MainForm.MainFormCloseAction.DeferUntilOperationCompletes, action);
    }
}
