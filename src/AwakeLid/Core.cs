using System.Text.Json;

namespace AwakeLid;

public enum Supply { Ac = 1, Dc = 2 }
public interface IPowerStore
{
    Guid ActivePlan { get; }
    uint Read(Guid plan, Supply supply);
    void Write(Guid plan, Supply supply, uint value);
}
public sealed class Lease
{
    public Guid Plan { get; set; }
    public Supply Supply { get; set; }
    public uint Original { get; set; }
    public uint Written { get; set; }
}
public sealed class SavedState
{
    public int Format { get; set; } = 1;
    public int Selection { get; set; }
    public List<Lease> Leases { get; set; } = [];
}
public interface IStateStore { void Save(SavedState state); }
public sealed class JsonStateStore(string path) : IStateStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public SavedState Load()
    {
        if (!File.Exists(path)) return new();
        var state = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(path))
            ?? throw new InvalidDataException("恢复记录为空，未修改电源设置。");
        if (state.Format != 1 || state.Selection is < 0 or > 3 ||
            state.Leases.Any(l => l.Plan == Guid.Empty || l.Supply is not (Supply.Ac or Supply.Dc) || l.Original > 3 || l.Written != 0))
            throw new InvalidDataException("恢复记录格式异常，未修改电源设置。");
        return state;
    }
    public void Save(SavedState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".new";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, state, Options);
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temp, path, path + ".previous");
        else File.Move(temp, path);
    }
}

// Only the guardian writes power policy. A durable lease is committed BEFORE each write.
public sealed class Coordinator(IPowerStore power, IStateStore persistence, SavedState state)
{
    public SavedState State { get; } = state;
    public int Suppressed { get; private set; }
    public bool IsActive { get; private set; }
    public string Status { get; private set; } = "跟随系统设置";
    public Action<string>? Log { get; set; }

    public void Select(int mask)
    {
        if (mask is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(mask));
        Suppressed &= ~mask; // An explicit new click may take ownership again.
        State.Selection = mask;
        persistence.Save(State);
    }
    public void Recover()
    {
        RestoreWhere(_ => true);
        IsActive = false;
    }
    public void Stop()
    {
        State.Selection = 0;
        persistence.Save(State);
        RestoreWhere(_ => true);
        IsActive = false;
        Status = "跟随系统设置";
    }
    public void Tick(bool awake)
    {
        // Detect external edits before either applying or restoring a lease.
        foreach (var lease in State.Leases.ToArray())
        {
            if (power.Read(lease.Plan, lease.Supply) != lease.Written)
            {
                Suppressed |= (int)lease.Supply;
                State.Leases.Remove(lease);
                persistence.Save(State);
                Log?.Invoke($"保留外部修改：{lease.Plan}/{lease.Supply}；本次会话暂停该项联动。");
            }
        }
        if (!awake)
        {
            RestoreWhere(_ => true);
            IsActive = false;
            Suppressed = 0;
            Status = State.Selection == 0 ? "跟随系统设置" : "等待 Awake 开始";
            return;
        }
        IsActive = true;
        var plan = power.ActivePlan;
        var effective = State.Selection & ~Suppressed;
        RestoreWhere(l => l.Plan != plan || (effective & (int)l.Supply) == 0);
        foreach (var supply in new[] { Supply.Ac, Supply.Dc })
        {
            if ((effective & (int)supply) == 0 || State.Leases.Any(l => l.Plan == plan && l.Supply == supply)) continue;
            var original = power.Read(plan, supply);
            // Even a pre-existing zero gets a lease: a later manual edit must suspend this session.
            var lease = new Lease { Plan = plan, Supply = supply, Original = original, Written = 0 };
            State.Leases.Add(lease);
            persistence.Save(State);
            if (original != 0)
            {
                power.Write(plan, supply, 0);
                if (power.Read(plan, supply) != 0) throw new IOException("合盖设置写入后校验失败。");
                Log?.Invoke($"临时启用：{plan}/{supply}；原值 {original}。");
            }
        }
        Status = State.Selection == 0 ? "跟随系统设置" : Suppressed != 0 ? "部分设置已由用户调整" : "随 Awake 生效中";
    }
    void RestoreWhere(Func<Lease, bool> predicate)
    {
        foreach (var lease in State.Leases.Where(predicate).ToArray())
        {
            var current = power.Read(lease.Plan, lease.Supply);
            if (current == lease.Written && current != lease.Original)
            {
                power.Write(lease.Plan, lease.Supply, lease.Original);
                if (power.Read(lease.Plan, lease.Supply) != lease.Original)
                    throw new IOException("恢复合盖设置后校验失败；已保留恢复记录。");
                Log?.Invoke($"已恢复：{lease.Plan}/{lease.Supply} → {lease.Original}。");
            }
            else if (current != lease.Written) Log?.Invoke($"保留最新系统设置：{lease.Plan}/{lease.Supply} → {current}。");
            State.Leases.Remove(lease);
            persistence.Save(State);
        }
    }
}
