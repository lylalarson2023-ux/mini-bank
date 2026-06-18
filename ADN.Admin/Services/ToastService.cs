namespace ADN_pay.Admin.Services;

public enum ToastLevel { Success, Error, Info }

/// Notifications éphémères pour la zone admin (équivalent local du NotificationService
/// du web, mais sans dépendance au projet web). Scoped : un état par circuit Blazor.
public class ToastService
{
    public event Action? OnChange;
    private readonly List<ToastMessage> _messages = new();
    public IReadOnlyList<ToastMessage> Messages => _messages;

    public void Show(string text, ToastLevel level = ToastLevel.Info)
    {
        var msg = new ToastMessage(Guid.NewGuid(), text, level);
        _messages.Add(msg);
        OnChange?.Invoke();
        _ = AutoRemoveAsync(msg.Id);
    }

    public void Success(string text) => Show(text, ToastLevel.Success);
    public void Error(string text) => Show(text, ToastLevel.Error);

    public void Remove(Guid id)
    {
        if (_messages.RemoveAll(m => m.Id == id) > 0)
            OnChange?.Invoke();
    }

    private async Task AutoRemoveAsync(Guid id)
    {
        await Task.Delay(4500);
        Remove(id);
    }

    public record ToastMessage(Guid Id, string Text, ToastLevel Level);
}
