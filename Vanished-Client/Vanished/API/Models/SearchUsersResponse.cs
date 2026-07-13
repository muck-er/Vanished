using System.Collections.Generic;

namespace Vanished.API.Models
{
    public class SearchUsersResponse
    {
        public bool success { get; set; }
        public string message { get; set; } = string.Empty;
        public List<PublicUser> users { get; set; } = new();
    }
}
