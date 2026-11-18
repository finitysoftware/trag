namespace Trag.Domain.Sync;

public sealed record SyncRemote(string Name, string FetchAddress, string PushAddress, string ConfigurationId, string Limitation = "");
