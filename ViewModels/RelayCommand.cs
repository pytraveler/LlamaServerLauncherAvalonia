using System;

namespace LlamaServerLauncher.ViewModels;

public class CommandAdapter : System.Windows.Input.ICommand
{
    private readonly ICommand _innerCommand;
    
    public CommandAdapter(ICommand innerCommand)
    {
        _innerCommand = innerCommand;
    }
    
    public event EventHandler? CanExecuteChanged
    {
        add => _innerCommand.CanExecuteChanged += value;
        remove => _innerCommand.CanExecuteChanged -= value;
    }
    
    public bool CanExecute(object? parameter) => _innerCommand.CanExecute(parameter);
    
    public void Execute(object? parameter) => _innerCommand.Execute(parameter);
}

public interface ICommand
{
    bool CanExecute(object? parameter);
    void Execute(object? parameter);
    event EventHandler? CanExecuteChanged;
}

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute != null ? _ => canExecute() : null)
    {
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return _canExecute == null || _canExecute(parameter);
    }

    public void Execute(object? parameter)
    {
        _execute(parameter);
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

public class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, System.Threading.Tasks.Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<object?, System.Threading.Tasks.Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public AsyncRelayCommand(Func<System.Threading.Tasks.Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute != null ? _ => canExecute() : null)
    {
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return !_isExecuting && (_canExecute == null || _canExecute(parameter));
    }

    public async void Execute(object? parameter)
    {
        if (_isExecuting) return;

        _isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            await _execute(parameter);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}