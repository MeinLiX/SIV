using ProtoBuf;

namespace SIV.Infrastructure.Protos;

[ProtoContract]
public class GCMsgClientHello : IExtensible
{
    [ProtoMember(1)]
    public uint Version { get; set; }

    private IExtension? _extensionObject;
    IExtension IExtensible.GetExtensionObject(bool createIfMissing)
        => Extensible.GetExtensionObject(ref _extensionObject, createIfMissing);
}

[ProtoContract]
public class CMsgLegacySource1ClientWelcome : IExtensible
{
    [ProtoMember(1)]
    public uint Version { get; set; }

    [ProtoMember(2)]
    public byte[]? GameData { get; set; }

    [ProtoMember(3)]
    public List<CMsgSOCacheSubscribed> Outofdate_Subscribed_Caches { get; set; } = [];

    [ProtoMember(4)]
    public List<CMsgSOCacheSubscriptionCheck> Uptodate_Subscribed_Caches { get; set; } = [];

    private IExtension? _extensionObject;
    IExtension IExtensible.GetExtensionObject(bool createIfMissing)
        => Extensible.GetExtensionObject(ref _extensionObject, createIfMissing);
}

[ProtoContract]
public class CMsgGCCStrike15Welcome : IExtensible
{
    [ProtoMember(5)]
    public uint StoreItemHash { get; set; }

    [ProtoMember(18)]
    public ulong GsCookieId { get; set; }

    [ProtoMember(19)]
    public ulong UniqueId { get; set; }

    private IExtension? _extensionObject;
    IExtension IExtensible.GetExtensionObject(bool createIfMissing)
        => Extensible.GetExtensionObject(ref _extensionObject, createIfMissing);
}

[ProtoContract]
public class CMsgSOCacheSubscriptionCheck : IExtensible
{
    private IExtension? _extensionObject;
    IExtension IExtensible.GetExtensionObject(bool createIfMissing)
        => Extensible.GetExtensionObject(ref _extensionObject, createIfMissing);
}

[ProtoContract]
public class CMsgSOCacheSubscribed : IExtensible
{
    [ProtoMember(2)]
    public List<SubscribedType> Objects { get; set; } = [];

    private IExtension? _extensionObject;
    IExtension IExtensible.GetExtensionObject(bool createIfMissing)
        => Extensible.GetExtensionObject(ref _extensionObject, createIfMissing);

    [ProtoContract]
    public class SubscribedType : IExtensible
    {
        [ProtoMember(1)]
        public int TypeId { get; set; }

        [ProtoMember(2)]
        public List<byte[]> ObjectData { get; set; } = [];

        private IExtension? _extensionObject;
        IExtension IExtensible.GetExtensionObject(bool createIfMissing)
            => Extensible.GetExtensionObject(ref _extensionObject, createIfMissing);
    }
}

[ProtoContract]
public class CMsgGCCStrike15_v2_MatchmakingClient2GCHello : IExtensible
{
    private IExtension? _extensionObject;
    IExtension IExtensible.GetExtensionObject(bool createIfMissing)
        => Extensible.GetExtensionObject(ref _extensionObject, createIfMissing);
}

[ProtoContract]
public class CMsgGCCStrike15_v2_MatchmakingGC2ClientHello : IExtensible
{
    [ProtoMember(1)]
    public uint AccountId { get; set; }

    private IExtension? _extensionObject;
    IExtension IExtensible.GetExtensionObject(bool createIfMissing)
        => Extensible.GetExtensionObject(ref _extensionObject, createIfMissing);
}

[ProtoContract]
public class CMsgGCCStrike15_v2_ClientLogonFatalError : IExtensible
{
    [ProtoMember(1)]
    public uint ErrorCode { get; set; }

    [ProtoMember(2)]
    public string? Message { get; set; }

    [ProtoMember(3)]
    public string? Country { get; set; }

    private IExtension? _extensionObject;
    IExtension IExtensible.GetExtensionObject(bool createIfMissing)
        => Extensible.GetExtensionObject(ref _extensionObject, createIfMissing);
}

[ProtoContract]
public class CSOEconItem
{
    [ProtoMember(1)]
    public ulong Id { get; set; }

    [ProtoMember(2)]
    public uint AccountId { get; set; }

    [ProtoMember(3)]
    public uint Inventory { get; set; }

    [ProtoMember(4)]
    public int DefIndex { get; set; }

    [ProtoMember(5)]
    public uint Quantity { get; set; }

    [ProtoMember(6)]
    public uint Level { get; set; }

    [ProtoMember(7)]
    public int Quality { get; set; }

    [ProtoMember(8)]
    public uint Flags { get; set; }

    [ProtoMember(9)]
    public uint Origin { get; set; }

    [ProtoMember(10)]
    public string? CustomName { get; set; }

    [ProtoMember(11)]
    public string? CustomDesc { get; set; }

    [ProtoMember(12)]
    public List<CSOEconItemAttribute> Attribute { get; set; } = [];

    [ProtoMember(14)]
    public bool InUse { get; set; }

    [ProtoMember(15)]
    public uint Style { get; set; }

    [ProtoMember(16)]
    public ulong OriginalId { get; set; }

    [ProtoMember(18)]
    public List<CSOEconItemEquipped> EquippedState { get; set; } = [];

    [ProtoMember(19)]
    public byte Rarity { get; set; }
}

[ProtoContract]
public class CSOEconItemAttribute
{
    [ProtoMember(1)]
    public uint DefIndex { get; set; }

    [ProtoMember(2)]
    public uint Value { get; set; }

    [ProtoMember(3)]
    public byte[] ValueBytes { get; set; } = [];
}

[ProtoContract]
public class CSOEconItemEquipped
{
    [ProtoMember(1)]
    public uint NewClass { get; set; }

    [ProtoMember(2)]
    public uint NewSlot { get; set; }
}

[ProtoContract]
public class CMsgCasketContents : IExtensible
{
    [ProtoMember(1)]
    public ulong CasketId { get; set; }

    [ProtoMember(2)]
    public List<CSOEconItem> Items { get; set; } = [];

    private IExtension? _extensionObject;
    IExtension IExtensible.GetExtensionObject(bool createIfMissing)
        => Extensible.GetExtensionObject(ref _extensionObject, createIfMissing);
}

[ProtoContract]
public class CMsgCasketItem : IExtensible
{
    [ProtoMember(1)]
    public ulong CasketItemId { get; set; }

    [ProtoMember(2)]
    public ulong ItemItemId { get; set; }

    private IExtension? _extensionObject;
    IExtension IExtensible.GetExtensionObject(bool createIfMissing)
        => Extensible.GetExtensionObject(ref _extensionObject, createIfMissing);
}

[ProtoContract]
public class CMsgGCItemCustomizationNotification : IExtensible
{
    [ProtoMember(1)]
    public List<ulong> ItemId { get; set; } = [];

    [ProtoMember(2)]
    public uint Request { get; set; }

    [ProtoMember(3)]
    public List<ulong> ExtraData { get; set; } = [];

    private IExtension? _extensionObject;
    IExtension IExtensible.GetExtensionObject(bool createIfMissing)
        => Extensible.GetExtensionObject(ref _extensionObject, createIfMissing);
}

[ProtoContract]
public class CMsgSOSingleObject : IExtensible
{
    [ProtoMember(2)]
    public int TypeId { get; set; }

    [ProtoMember(3)]
    public byte[] ObjectData { get; set; } = [];

    private IExtension? _extensionObject;
    IExtension IExtensible.GetExtensionObject(bool createIfMissing)
        => Extensible.GetExtensionObject(ref _extensionObject, createIfMissing);
}

[ProtoContract]
public class CMsgConnectionStatus : IExtensible
{
    [ProtoMember(1)]
    public int Status { get; set; }

    [ProtoMember(2)]
    public uint ClientSessionNeed { get; set; }

    [ProtoMember(3)]
    public int QueuePosition { get; set; }

    [ProtoMember(4)]
    public int QueueSize { get; set; }

    [ProtoMember(5)]
    public int WaitSeconds { get; set; }

    [ProtoMember(6)]
    public int EstimatedWaitSecondsRemaining { get; set; }

    private IExtension? _extensionObject;
    IExtension IExtensible.GetExtensionObject(bool createIfMissing)
        => Extensible.GetExtensionObject(ref _extensionObject, createIfMissing);
}
