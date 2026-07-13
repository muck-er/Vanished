using System.Collections.Generic;

namespace Vanished.API.Models;

public sealed class UserDevicesResponse
{
    public bool success { get; set; }
    public string message { get; set; } = string.Empty;
    public List<UserDeviceDescriptor> devices { get; set; } = new();
}

public sealed class UserDeviceDescriptor
{
    public string device_id { get; set; } = string.Empty;
    public string device_encryption_public_key { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public string platform { get; set; } = string.Empty;
    public string last_seen_at { get; set; } = string.Empty;
}


public sealed class MyDevicesResponse
{
    public bool success { get; set; }
    public string message { get; set; } = string.Empty;
    public List<MyDeviceDescriptor> devices { get; set; } = new();
}

public sealed class MyDeviceDescriptor
{
    public string device_id { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public string platform { get; set; } = string.Empty;
    public bool is_trusted { get; set; }
    public bool is_current { get; set; }
    public string created_at { get; set; } = string.Empty;
    public string last_seen_at { get; set; } = string.Empty;
    public string revoked_at { get; set; } = string.Empty;
}

public sealed class RevokeDevicesResponse : ApiResponse
{
    public int revoked_count { get; set; }
}

public sealed class ContactIdentityResponse
{
    public bool success { get; set; }
    public string message { get; set; } = string.Empty;
    public int user_id { get; set; }
    public string username { get; set; } = string.Empty;
    public int key_version { get; set; }
    public string identity_public_key { get; set; } = string.Empty;
    public string identity_fingerprint { get; set; } = string.Empty;
    public string safety_number { get; set; } = string.Empty;
    public bool verified { get; set; }
    public string verified_at { get; set; } = string.Empty;
}

public sealed class ContactIdentityVerificationResponse
{
    public bool success { get; set; }
    public string message { get; set; } = string.Empty;
    public bool verified { get; set; }
    public string current_fingerprint { get; set; } = string.Empty;
    public string verified_fingerprint { get; set; } = string.Empty;
    public int current_key_version { get; set; }
    public int verified_key_version { get; set; }
    public string verified_at { get; set; } = string.Empty;
}
