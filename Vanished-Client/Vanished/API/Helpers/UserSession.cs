using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Vanished.API.Helpers;

public sealed class UserSession : INotifyPropertyChanged
{
    public static UserSession Current { get; } = new();

    private int _userId;
    private string _email = string.Empty;
    private string _username = string.Empty;
    private string _displayName = string.Empty;
    private string _bio = string.Empty;
    private string _avatarBase64 = string.Empty;
    private string _avatarMime = string.Empty;

    public int UserId
    {
        get => _userId;
        set => SetField(ref _userId, value);
    }

    public string Email
    {
        get => _email;
        set => SetField(ref _email, value ?? string.Empty);
    }

    public string Username
    {
        get => _username;
        set
        {
            if (SetField(ref _username, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(DisplayLabel));
                OnPropertyChanged(nameof(AvatarInitial));
            }
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetField(ref _displayName, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(DisplayLabel));
                OnPropertyChanged(nameof(AvatarInitial));
            }
        }
    }

    public string Bio
    {
        get => _bio;
        set => SetField(ref _bio, value ?? string.Empty);
    }

    public string AvatarBase64
    {
        get => _avatarBase64;
        set => SetField(ref _avatarBase64, value ?? string.Empty);
    }

    public string AvatarMime
    {
        get => _avatarMime;
        set => SetField(ref _avatarMime, value ?? string.Empty);
    }

    public string DisplayLabel => string.IsNullOrWhiteSpace(DisplayName)
        ? (string.IsNullOrWhiteSpace(Username) ? "Vanished" : Username)
        : DisplayName;

    public string AvatarInitial => string.IsNullOrWhiteSpace(DisplayLabel)
        ? "V"
        : DisplayLabel.Trim()[0].ToString().ToUpperInvariant();

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetProfile(int userId, string email, string username, string displayName, string bio, string avatarBase64, string avatarMime)
    {
        UserId = userId;
        Email = email;
        Username = username;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName;
        Bio = bio;
        AvatarBase64 = avatarBase64;
        AvatarMime = avatarMime;
    }

    public void Clear()
    {
        UserId = 0;
        Email = string.Empty;
        Username = string.Empty;
        DisplayName = string.Empty;
        Bio = string.Empty;
        AvatarBase64 = string.Empty;
        AvatarMime = string.Empty;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
