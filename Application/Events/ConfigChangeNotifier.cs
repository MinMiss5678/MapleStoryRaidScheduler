using System;

namespace Application.Events;

public class ConfigChangeNotifier
{
    public event Action? OnChanged;

    public void Notify() => OnChanged?.Invoke();
}
