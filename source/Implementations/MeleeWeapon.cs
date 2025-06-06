﻿using CombatOverhaul.Animations;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.Inputs;
using CombatOverhaul.Integration;
using CombatOverhaul.MeleeSystems;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.RangedSystems.Aiming;
using OpenTK.Mathematics;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace CombatOverhaul.Implementations;

public enum MeleeWeaponState
{
    Idle,
    WindingUp,
    Attacking,
    Parrying,
    Blocking,
    Cooldown,
    StartingAim,
    Aiming,
    Throwing
}

public enum MeleeWeaponStance
{
    MainHand,
    OffHand,
    TwoHanded
}

public interface IHasMeleeWeaponActions
{
    bool CanAttack(bool mainHand);
    bool CanBlock(bool mainHand);
}

public class StanceStats
{
    public bool CanAttack { get; set; } = false;
    public bool CanParry { get; set; } = false;
    public bool CanBlock { get; set; } = false;
    public bool CanSprint { get; set; } = true;
    public bool CanThrow { get; set; } = false;
    public float SpeedPenalty { get; set; } = 0;
    public float BlockSpeedPenalty { get; set; } = 0;

    public float GripLengthFactor { get; set; } = 0;
    public float GripMinLength { get; set; } = 0;
    public float GripMaxLength { get; set; } = 0;

    public MeleeAttackStats? Attack { get; set; }
    public Dictionary<string, MeleeAttackStats>? DirectionalAttacks { get; set; }
    public DamageBlockJson? Block { get; set; }
    public DamageBlockJson? Parry { get; set; }
    public MeleeAttackStats? HandleAttack { get; set; }

    public string? AttackHitSound { get; set; } = null;
    public string? HandleHitSound { get; set; } = null;

    public float AttackCooldownMs { get; set; } = 0;
    public float BlockCooldownMs { get; set; } = 0;

    public string AttackDirectionsType { get; set; } = "None";
    public Dictionary<string, string[]> AttackAnimation { get; set; } = new();
    public string BlockAnimation { get; set; } = "";
    public string ReadyAnimation { get; set; } = "";
    public string IdleAnimation { get; set; } = "";

    public Dictionary<string, string> TpAttackAnimation { get; set; } = new();
    public string BlockTpAnimation { get; set; } = "";
}

public class ThrowWeaponStats
{
    public string AimAnimation { get; set; } = "";
    public string ThrowAnimation { get; set; } = "";
    public string TpAimAnimation { get; set; } = "";
    public string TpThrowAnimation { get; set; } = "";

    public AimingStatsJson Aiming { get; set; } = new();
    public float DamageStrength { get; set; }
    public float Knockback { get; set; } = 0;
    public int DurabilityDamage { get; set; } = 1;
    public float Velocity { get; set; } = 1;
    public float Zeroing { get; set; } = 1.5f;
}

public class MeleeWeaponStats : WeaponStats
{
    public StanceStats? OneHandedStance { get; set; } = null;
    public StanceStats? TwoHandedStance { get; set; } = null;
    public StanceStats? OffHandStance { get; set; } = null;
    public ThrowWeaponStats? ThrowAttack { get; set; } = null;
    public float ScreenShakeStrength { get; set; } = 0.25f;
    public float ThrowScreenShakeStrength { get; set; } = 0.15f;
    public bool RenderingOffset { get; set; } = false;
    public float AnimationStaggerOnHitDurationMs { get; set; } = 100;
}

public readonly struct ItemStackMeleeWeaponStats
{
    public readonly float DamageMultiplier;
    public readonly float DamageBonus;
    public readonly int DamageTierBonus;
    public readonly float AttackSpeed;
    public readonly int BlockTierBonus;
    public readonly int ParryTierBonus;
    public readonly float ThrownDamageMultiplier;
    public readonly int ThrownDamageTierBonus;
    public readonly float ThrownAimingDifficulty;
    public readonly float ThrownProjectileSpeedMultiplier;
    public readonly float KnockbackMultiplier;

    public ItemStackMeleeWeaponStats(float damageMultiplier, float damageBonus, int damageTierBonus, float attackSpeed, int blockTierBonus, int parryTierBonus, float thrownDamageMultiplier, int thrownDamageTierBonus, float thrownAimingDifficulty, float thrownProjectileSpeedMultiplier, float knockbackMultiplier)
    {
        DamageMultiplier = damageMultiplier;
        DamageBonus = damageBonus;
        DamageTierBonus = damageTierBonus;
        AttackSpeed = attackSpeed;
        BlockTierBonus = blockTierBonus;
        ParryTierBonus = parryTierBonus;
        ThrownDamageMultiplier = thrownDamageMultiplier;
        ThrownDamageTierBonus = thrownDamageTierBonus;
        ThrownAimingDifficulty = thrownAimingDifficulty;
        ThrownProjectileSpeedMultiplier = thrownProjectileSpeedMultiplier;
        KnockbackMultiplier = knockbackMultiplier;
    }

    public ItemStackMeleeWeaponStats()
    {
        DamageMultiplier = 1;
        DamageBonus = 0;
        DamageTierBonus = 0;
        AttackSpeed = 1;
        BlockTierBonus = 0;
        ParryTierBonus = 0;
        ThrownDamageMultiplier = 1;
        ThrownDamageTierBonus = 0;
        ThrownAimingDifficulty = 1;
        ThrownProjectileSpeedMultiplier = 1;
        KnockbackMultiplier = 1;
    }

    public static ItemStackMeleeWeaponStats FromItemStack(ItemStack stack)
    {
        float damageMultiplier = stack.Attributes.GetFloat("damageMultiplier", 1);
        float damageBonus = stack.Attributes.GetFloat("damageBonus", 0);
        int damageTierBonus = stack.Attributes.GetInt("damageTierBonus", 0);
        float attackSpeed = stack.Attributes.GetFloat("attackSpeed", 1);
        int blockTierBonus = stack.Attributes.GetInt("blockTierBonus", 0);
        int parryTierBonus = stack.Attributes.GetInt("parryTierBonus", 0);
        float thrownDamageMultiplier = stack.Attributes.GetFloat("thrownDamageMultiplier", 1);
        int thrownDamageTierBonus = stack.Attributes.GetInt("thrownDamageTierBonus", 0);
        float thrownAimingDifficulty = stack.Attributes.GetFloat("thrownAimingDifficulty", 1);
        float thrownProjectileSpeedMultiplier = stack.Attributes.GetFloat("thrownProjectileSpeedMultiplier", 1);
        float knockbackMultiplier = stack.Attributes.GetFloat("knockbackMultiplier", 1);

        return new ItemStackMeleeWeaponStats(damageMultiplier, damageBonus, damageTierBonus, attackSpeed, blockTierBonus, parryTierBonus, thrownDamageMultiplier, thrownDamageTierBonus, thrownAimingDifficulty, thrownProjectileSpeedMultiplier, knockbackMultiplier);
    }
    public static float GetAttackSpeed(ItemStack stack) => stack.Attributes.GetFloat("attackSpeed", 1);
}

public class MeleeWeaponClient : IClientWeaponLogic, IHasDynamicIdleAnimations, IOnGameTick, IRestrictAction
{
    public MeleeWeaponClient(ICoreClientAPI api, Item item)
    {
        Item = item;
        Api = api;

        CombatOverhaulSystem system = api.ModLoader.GetModSystem<CombatOverhaulSystem>();
        MeleeBlockSystem = system.ClientBlockSystem ?? throw new Exception();
        SoundsSystem = system.ClientSoundsSynchronizer ?? throw new Exception();
        RangedWeaponSystem = system.ClientRangedWeaponSystem ?? throw new Exception();
        AimingSystem = system.AimingSystem ?? throw new Exception();
        Settings = system.Settings;

        Stats = item.Attributes.AsObject<MeleeWeaponStats>();
        AimingStats = Stats.ThrowAttack?.Aiming.ToStats();

        if (Stats.OneHandedStance?.Attack != null)
        {
            OneHandedAttack = new(api, Stats.OneHandedStance.Attack);
            RegisterCollider(item.Code.ToString(), "onehanded-", OneHandedAttack);
        }
        else if (Stats.OneHandedStance?.DirectionalAttacks != null)
        {
            DirectionalOneHandedAttacks = new();
            foreach ((string direction, MeleeAttackStats attack) in Stats.OneHandedStance.DirectionalAttacks)
            {
                DirectionalOneHandedAttacks.Add(Enum.Parse<AttackDirection>(direction), new(api, attack));
                RegisterCollider(item.Code.ToString(), $"onehanded-{direction}-", DirectionalOneHandedAttacks[Enum.Parse<AttackDirection>(direction)]);
            }
        }

        if (Stats.TwoHandedStance?.Attack != null)
        {
            TwoHandedAttack = new(api, Stats.TwoHandedStance.Attack);
            RegisterCollider(item.Code.ToString(), "twohanded-", TwoHandedAttack);
        }
        else if (Stats.TwoHandedStance?.DirectionalAttacks != null)
        {
            DirectionalTwoHandedAttacks = new();
            foreach ((string direction, MeleeAttackStats attack) in Stats.TwoHandedStance.DirectionalAttacks)
            {
                DirectionalTwoHandedAttacks.Add(Enum.Parse<AttackDirection>(direction), new(api, attack));
                RegisterCollider(item.Code.ToString(), $"twohanded-{direction}-", DirectionalTwoHandedAttacks[Enum.Parse<AttackDirection>(direction)]);
            }
        }

        if (Stats.OffHandStance?.Attack != null)
        {
            OffHandAttack = new(api, Stats.OffHandStance.Attack);
            RegisterCollider(item.Code.ToString(), "offhand-", OffHandAttack);
        }
        else if (Stats.OffHandStance?.DirectionalAttacks != null)
        {
            DirectionalOffHandAttacks = new();
            foreach ((string direction, MeleeAttackStats attack) in Stats.OffHandStance.DirectionalAttacks)
            {
                DirectionalOffHandAttacks.Add(Enum.Parse<AttackDirection>(direction), new(api, attack));
                RegisterCollider(item.Code.ToString(), $"offhand-{direction}-", DirectionalOffHandAttacks[Enum.Parse<AttackDirection>(direction)]);
            }
        }

        if (Stats.OneHandedStance?.HandleAttack != null)
        {
            OneHandedHandleAttack = new(api, Stats.OneHandedStance.HandleAttack);
            RegisterCollider(item.Code.ToString(), "onehanded-handle-", OneHandedHandleAttack);
        }
        if (Stats.TwoHandedStance?.HandleAttack != null)
        {
            TwoHandedHandleAttack = new(api, Stats.TwoHandedStance.HandleAttack);
            RegisterCollider(item.Code.ToString(), "twohanded-handle-", TwoHandedHandleAttack);
        }
        if (Stats.OffHandStance?.HandleAttack != null)
        {
            OffHandHandleAttack = new(api, Stats.OffHandStance.HandleAttack);
            RegisterCollider(item.Code.ToString(), "offhand-handle-", OffHandHandleAttack);
        }
    }

    public int ItemId => Item.Id;

    public virtual DirectionsConfiguration DirectionsType { get; protected set; } = DirectionsConfiguration.None;

    public AnimationRequestByCode? GetIdleAnimation(bool mainHand)
    {
        return GetStance<MeleeWeaponStance>(mainHand) switch
        {
            MeleeWeaponStance.MainHand => Stats?.OneHandedStance?.IdleAnimation == null ? null : new(Stats.OneHandedStance.IdleAnimation, 1, 1, global::CombatOverhaul.Implementations.MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.OffHand => Stats?.OffHandStance?.IdleAnimation == null ? null : new(Stats.OffHandStance.IdleAnimation, 1, 1, global::CombatOverhaul.Implementations.MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.TwoHanded => Stats?.TwoHandedStance?.IdleAnimation == null ? null : new(Stats.TwoHandedStance.IdleAnimation, 1, 1, global::CombatOverhaul.Implementations.MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            _ => null
        };
    }
    public AnimationRequestByCode? GetReadyAnimation(bool mainHand)
    {
        return GetStance<MeleeWeaponStance>(mainHand) switch
        {
            MeleeWeaponStance.MainHand => Stats?.OneHandedStance?.ReadyAnimation == null ? null : new(Stats.OneHandedStance.ReadyAnimation, 1, 1, global::CombatOverhaul.Implementations.MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.OffHand => Stats?.OffHandStance?.ReadyAnimation == null ? null : new(Stats.OffHandStance.ReadyAnimation, 1, 1, global::CombatOverhaul.Implementations.MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.TwoHanded => Stats?.TwoHandedStance?.ReadyAnimation == null ? null : new(Stats.TwoHandedStance.ReadyAnimation, 1, 1, global::CombatOverhaul.Implementations.MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            _ => null,
        };
    }

    public virtual void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {
        EnsureStance(player, mainHand);
        SetState(MeleeWeaponState.Idle, mainHand);
        SetSpeedPenalty(mainHand, player);
    }
    public virtual void OnDeselected(EntityPlayer player, bool mainHand, ref int state)
    {
        MeleeBlockSystem.StopBlock(mainHand);
        StopAttackCooldown(mainHand);
        StopBlockCooldown(mainHand);
        GripController?.ResetGrip(mainHand);
        AnimationBehavior?.StopSpeedModifier();
        PlayerBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory);
        AimingAnimationController?.Stop(mainHand);
        AimingSystem.StopAiming();
        AnimationBehavior?.StopAllVanillaAnimations(mainHand);
    }
    public virtual void OnRegistered(ActionsManagerPlayerBehavior behavior, ICoreClientAPI api)
    {
        PlayerBehavior = behavior;
        AnimationBehavior = behavior.entity.GetBehavior<FirstPersonAnimationsBehavior>();
        TpAnimationBehavior = behavior.entity.GetBehavior<ThirdPersonAnimationsBehavior>();
        GripController = new(AnimationBehavior);

        if (AimingStats != null) AimingAnimationController = new(AimingSystem, AnimationBehavior, AimingStats);
    }

    public virtual void RenderDebugCollider(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        if (DebugWindowManager._currentCollider != null)
        {
            DebugWindowManager._currentCollider.Value.Transform(byPlayer.Entity.Pos, byPlayer.Entity, inSlot, Api, right: true)?.Render(Api, byPlayer.Entity, ColorUtil.ColorFromRgba(255, 125, 125, 255));
            return;
        }

        MeleeAttack? attack = GetStanceAttack(true, CurrentMainHandDirection);
        if (attack != null)
        {
            foreach (MeleeDamageType damageType in attack.DamageTypes)
            {
                damageType.RelativeCollider.Transform(byPlayer.Entity.Pos, byPlayer.Entity, inSlot, Api, right: true)?.Render(Api, byPlayer.Entity);
            }
        }

        MeleeAttack? handle = GetStanceHandleAttack(mainHand: true);
        if (handle != null)
        {
            foreach (MeleeDamageType damageType in handle.DamageTypes)
            {
                damageType.RelativeCollider.Transform(byPlayer.Entity.Pos, byPlayer.Entity, inSlot, Api, right: true)?.Render(Api, byPlayer.Entity, ColorUtil.ColorFromRgba(150, 150, 150, 255));
            }
        }
    }

    public virtual bool OnMouseWheel(ItemSlot slot, IClientPlayer byPlayer, float delta)
    {
        if (PlayerBehavior?.ActionListener.IsActive(EnumEntityAction.RightMouseDown) == false) return false;

        bool mainHand = byPlayer.Entity.RightHandItemSlot == slot;
        StanceStats? stance = GetStanceStats(mainHand);
        float canChangeGrip = stance?.GripLengthFactor ?? 0;

        if (canChangeGrip != 0 && stance != null)
        {
            GripController?.ChangeGrip(delta, mainHand, canChangeGrip, stance.GripMinLength, stance.GripMaxLength);
            return true;
        }
        else
        {
            GripController?.ResetGrip(mainHand);
            return false;
        }
    }

    public bool CanAttack(bool mainHand = true) => GetStanceStats(mainHand)?.CanAttack ?? false;
    public bool CanBlock(bool mainHand = true) => GetStanceStats(mainHand)?.CanBlock ?? false;
    public bool CanParry(bool mainHand = true) => GetStanceStats(mainHand)?.CanParry ?? false;
    public bool CanThrow(bool mainHand = true) => GetStanceStats(mainHand)?.CanThrow ?? false;

    public virtual void OnGameTick(ItemSlot slot, EntityPlayer player, ref int state, bool mainHand)
    {
        if (!mainHand && CanAttackWithOtherHand(player, mainHand)) return;
        EnsureStance(player, mainHand);
        if (!CanAttack(mainHand)) return;
        if (IsAttackOnCooldown(mainHand)) return;
        if (GetState<MeleeWeaponState>(!mainHand) == MeleeWeaponState.Blocking) return;

        MeleeAttack? attack = GetStanceAttack(mainHand, mainHand ? CurrentMainHandDirection : CurrentOffHandDirection);
        StanceStats? stats = GetStanceStats(mainHand);
        MeleeAttack? handle = GetStanceHandleAttack(mainHand);

        if (attack == null || stats == null) return;

        switch (GetState<MeleeWeaponState>(mainHand))
        {
            case MeleeWeaponState.Idle:
                break;
            case MeleeWeaponState.WindingUp:
                break;
            case MeleeWeaponState.Cooldown:
                break;
            case MeleeWeaponState.Attacking:
                {
                    TryAttack(attack, handle, stats, slot, player, mainHand);
                }
                break;
            default:
                break;
        }
    }

    public void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (Stats.OneHandedStance?.Attack != null && Stats.OneHandedStance.Attack.DamageTypes.Length > 0)
        {
            string description = GetAttackStatsDescription(inSlot, Stats.OneHandedStance.Attack.DamageTypes.Select(element => element.Damage), "combatoverhaul:iteminfo-melee-weapon-onehanded");
            dsc.AppendLine(description);
        }
        else if (Stats.OneHandedStance?.DirectionalAttacks != null && Stats.OneHandedStance.DirectionalAttacks.Count > 0)
        {
            IEnumerable<DamageDataJson> damageTypes = Stats.OneHandedStance.DirectionalAttacks.Values.SelectMany(element => element.DamageTypes).Select(element => element.Damage);
            string description = GetAttackStatsDescription(inSlot, damageTypes, "combatoverhaul:iteminfo-melee-weapon-onehanded");
            dsc.AppendLine(description);
        }

        if (Stats.TwoHandedStance?.Attack != null && Stats.TwoHandedStance.Attack.DamageTypes.Length > 0)
        {
            string description = GetAttackStatsDescription(inSlot, Stats.TwoHandedStance.Attack.DamageTypes.Select(element => element.Damage), "combatoverhaul:iteminfo-melee-weapon-twohanded");
            dsc.AppendLine(description);
        }
        else if (Stats.TwoHandedStance?.DirectionalAttacks != null && Stats.TwoHandedStance.DirectionalAttacks.Count > 0)
        {
            IEnumerable<DamageDataJson> damageTypes = Stats.TwoHandedStance.DirectionalAttacks.Values.SelectMany(element => element.DamageTypes).Select(element => element.Damage);
            string description = GetAttackStatsDescription(inSlot, damageTypes, "combatoverhaul:iteminfo-melee-weapon-twohanded");
            dsc.AppendLine(description);
        }

        if (Stats.OffHandStance?.Attack != null && Stats.OffHandStance.Attack.DamageTypes.Length > 0)
        {
            string description = GetAttackStatsDescription(inSlot, Stats.OffHandStance.Attack.DamageTypes.Select(element => element.Damage), "combatoverhaul:iteminfo-melee-weapon-offhanded");
            dsc.AppendLine(description);
        }
        else if (Stats.OffHandStance?.DirectionalAttacks != null && Stats.OffHandStance.DirectionalAttacks.Count > 0)
        {
            IEnumerable<DamageDataJson> damageTypes = Stats.OffHandStance.DirectionalAttacks.Values.SelectMany(element => element.DamageTypes).Select(element => element.Damage);
            string description = GetAttackStatsDescription(inSlot, damageTypes, "combatoverhaul:iteminfo-melee-weapon-offhanded");
            dsc.AppendLine(description);
        }

        ItemStackMeleeWeaponStats stackStats = ItemStackMeleeWeaponStats.FromItemStack(inSlot.Itemstack);

        if (Stats.OneHandedStance?.Block?.BlockTier != null)
        {
            dsc.AppendLine();
            float blockTier = 0;
            foreach ((string damageType, float tier) in Stats.OneHandedStance.Block.BlockTier)
            {
                blockTier = Math.Max(blockTier, tier) + stackStats.BlockTierBonus;
            }

            string bodyParts = Stats.OneHandedStance.Block.Zones.Length == 12 ? Lang.Get("combatoverhaul:detailed-damage-zone-All") : Stats.OneHandedStance.Block.Zones
                .Select(zone => Lang.Get($"combatoverhaul:detailed-damage-zone-{zone}"))
                .Aggregate((first, second) => $"{first}, {second}");

            dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-melee-weapon-blockStats", $"{blockTier:F0}", bodyParts, Lang.Get("combatoverhaul:iteminfo-melee-weapon-onehanded-block")));
        }

        if (Stats.OneHandedStance?.Parry?.BlockTier != null)
        {
            dsc.AppendLine();
            float blockTier = 0;
            foreach ((string damageType, float tier) in Stats.OneHandedStance.Parry.BlockTier)
            {
                blockTier = Math.Max(blockTier, tier) + stackStats.ParryTierBonus;
            }

            string bodyParts = Stats.OneHandedStance.Parry.Zones.Length == 12 ? Lang.Get("combatoverhaul:detailed-damage-zone-All") : Stats.OneHandedStance.Parry.Zones
                .Select(zone => Lang.Get($"combatoverhaul:detailed-damage-zone-{zone}"))
                .Aggregate((first, second) => $"{first}, {second}");

            dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-melee-weapon-parryStats", $"{blockTier:F0}", bodyParts, Lang.Get("combatoverhaul:iteminfo-melee-weapon-onehanded-block")));
        }

        if (Stats.TwoHandedStance?.Block?.BlockTier != null)
        {
            dsc.AppendLine();
            float blockTier = 0;
            foreach ((string damageType, float tier) in Stats.TwoHandedStance.Block.BlockTier)
            {
                blockTier = Math.Max(blockTier, tier) + stackStats.BlockTierBonus;
            }

            string bodyParts = Stats.TwoHandedStance.Block.Zones.Length == 12 ? Lang.Get("combatoverhaul:detailed-damage-zone-All") : Stats.TwoHandedStance.Block.Zones
                .Select(zone => Lang.Get($"combatoverhaul:detailed-damage-zone-{zone}"))
                .Aggregate((first, second) => $"{first}, {second}");

            dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-melee-weapon-blockStats", $"{blockTier:F0}", bodyParts, Lang.Get("combatoverhaul:iteminfo-melee-weapon-twohanded-block")));
        }

        if (Stats.TwoHandedStance?.Parry?.BlockTier != null)
        {
            dsc.AppendLine();
            float blockTier = 0;
            foreach ((string damageType, float tier) in Stats.TwoHandedStance.Parry.BlockTier)
            {
                blockTier = Math.Max(blockTier, tier) + stackStats.ParryTierBonus;
            }

            string bodyParts = Stats.TwoHandedStance.Parry.Zones.Length == 12 ? Lang.Get("combatoverhaul:detailed-damage-zone-All") : Stats.TwoHandedStance.Parry.Zones
                .Select(zone => Lang.Get($"combatoverhaul:detailed-damage-zone-{zone}"))
                .Aggregate((first, second) => $"{first}, {second}");

            dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-melee-weapon-parryStats", $"{blockTier:F0}", bodyParts, Lang.Get("combatoverhaul:iteminfo-melee-weapon-twohanded-block")));
        }

        if (Stats.OffHandStance?.Block?.BlockTier != null)
        {
            dsc.AppendLine();
            float blockTier = 0;
            foreach ((string damageType, float tier) in Stats.OffHandStance.Block.BlockTier)
            {
                blockTier = Math.Max(blockTier, tier) + stackStats.BlockTierBonus;
            }

            string bodyParts = Stats.OffHandStance.Block.Zones.Length == 12 ? Lang.Get("combatoverhaul:detailed-damage-zone-All") : Stats.OffHandStance.Block.Zones
                .Select(zone => Lang.Get($"combatoverhaul:detailed-damage-zone-{zone}"))
                .Aggregate((first, second) => $"{first}, {second}");

            dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-melee-weapon-blockStats", $"{blockTier:F0}", bodyParts, Lang.Get("combatoverhaul:iteminfo-melee-weapon-offhanded-block")));
        }

        if (Stats.OffHandStance?.Parry?.BlockTier != null)
        {
            dsc.AppendLine();
            float blockTier = 0;
            foreach ((string damageType, float tier) in Stats.OffHandStance.Parry.BlockTier)
            {
                blockTier = Math.Max(blockTier, tier) + stackStats.ParryTierBonus;
            }

            string bodyParts = Stats.OffHandStance.Parry.Zones.Length == 12 ? Lang.Get("combatoverhaul:detailed-damage-zone-All") : Stats.OffHandStance.Parry.Zones
                .Select(zone => Lang.Get($"combatoverhaul:detailed-damage-zone-{zone}"))
                .Aggregate((first, second) => $"{first}, {second}");

            dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-melee-weapon-parryStats", $"{blockTier:F0}", bodyParts, Lang.Get("combatoverhaul:iteminfo-melee-weapon-offhanded-block")));
        }
    }
    public bool RestrictRightHandAction() => !CheckState(false, MeleeWeaponState.Idle, MeleeWeaponState.Aiming, MeleeWeaponState.StartingAim);
    public bool RestrictLeftHandAction() => !CheckState(true, MeleeWeaponState.Idle, MeleeWeaponState.Aiming, MeleeWeaponState.StartingAim);

    protected readonly Item Item;
    protected readonly ICoreClientAPI Api;
    protected readonly MeleeBlockSystemClient MeleeBlockSystem;
    protected readonly RangedWeaponSystemClient RangedWeaponSystem;
    protected readonly ClientAimingSystem AimingSystem;
    protected FirstPersonAnimationsBehavior? AnimationBehavior;
    protected ThirdPersonAnimationsBehavior? TpAnimationBehavior;
    protected ActionsManagerPlayerBehavior? PlayerBehavior;
    protected SoundsSynchronizerClient SoundsSystem;
    protected AimingAnimationController? AimingAnimationController;
    protected GripController? GripController;
    protected Settings Settings;
    internal const int _maxStates = 100;
    protected const int MaxState = _maxStates;
    protected readonly MeleeWeaponStats Stats;
    protected const string PlayerStatsMainHandCategory = "CombatOverhaul:held-item-mainhand";
    protected const string PlayerStatsOffHandCategory = "CombatOverhaul:held-item-offhand";
    protected bool ParryButtonReleased = true;

    protected long MainHandAttackCooldownTimer = -1;
    protected long OffHandAttackCooldownTimer = -1;
    protected long MainHandBlockCooldownTimer = -1;
    protected long OffHandBlockCooldownTimer = -1;
    protected int MainHandAttackCounter = 0;
    protected int OffHandAttackCounter = 0;
    protected bool HandleHitTerrain = false;
    protected const bool EditColliders = false;
    protected AttackDirection CurrentMainHandDirection = AttackDirection.Top;
    protected AttackDirection CurrentOffHandDirection = AttackDirection.Top;

    protected readonly AimingStats? AimingStats;

    protected MeleeAttack? OneHandedAttack;
    protected MeleeAttack? TwoHandedAttack;
    protected MeleeAttack? OffHandAttack;
    protected Dictionary<AttackDirection, MeleeAttack>? DirectionalOneHandedAttacks;
    protected Dictionary<AttackDirection, MeleeAttack>? DirectionalTwoHandedAttacks;
    protected Dictionary<AttackDirection, MeleeAttack>? DirectionalOffHandAttacks;

    protected MeleeAttack? OneHandedHandleAttack;
    protected MeleeAttack? TwoHandedHandleAttack;
    protected MeleeAttack? OffHandHandleAttack;

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Active)]
    protected virtual bool Attack(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (eventData.AltPressed) return false;
        if (!mainHand && CanAttackWithOtherHand(player, mainHand)) return false;
        EnsureStance(player, mainHand);
        if (!CanAttack(mainHand)) return false;
        if (IsAttackOnCooldown(mainHand)) return false;
        if (ActionRestricted(player, mainHand)) return false;

        MeleeAttack? attack = GetStanceAttack(mainHand, direction);
        StanceStats? stats = GetStanceStats(mainHand);
        MeleeAttack? handle = GetStanceHandleAttack(mainHand);

        if (attack == null || stats == null) return false;

        switch (GetState<MeleeWeaponState>(mainHand))
        {
            case MeleeWeaponState.Idle:
                {
                    if (mainHand)
                    {
                        CurrentMainHandDirection = direction;
                    }
                    else
                    {
                        CurrentOffHandDirection = direction;
                    }

                    string attackAnimation =
                        DirectionsType == DirectionsConfiguration.None ?
                        stats.AttackAnimation["Main"][(mainHand ? MainHandAttackCounter : OffHandAttackCounter) % stats.AttackAnimation["Main"].Length] :
                        stats.AttackAnimation[direction.ToString()][(mainHand ? MainHandAttackCounter : OffHandAttackCounter) % stats.AttackAnimation[direction.ToString()].Length];

                    string? tpAttackAnimation = "";
                    if (DirectionsType == DirectionsConfiguration.None)
                    {
                        stats.TpAttackAnimation.TryGetValue("Main", out tpAttackAnimation);
                    }
                    else
                    {
                        stats.TpAttackAnimation.TryGetValue(direction.ToString(), out tpAttackAnimation);
                    }
                    tpAttackAnimation ??= "";

                    float animationSpeed = ItemStackMeleeWeaponStats.GetAttackSpeed(slot.Itemstack);
                    MeleeBlockSystem.StopBlock(mainHand);
                    SetState(MeleeWeaponState.WindingUp, mainHand);
                    attack.Start(player.Player);
                    handle?.Start(player.Player);
                    AnimationBehavior?.Play(
                        mainHand,
                        attackAnimation,
                        animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat) * animationSpeed,
                        category: AnimationCategory(mainHand),
                        callback: () => AttackAnimationCallback(mainHand),
                        callbackHandler: code => AttackAnimationCallbackHandler(code, mainHand));
                    TpAnimationBehavior?.Play(
                        mainHand,
                        attackAnimation,
                        animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat) * animationSpeed,
                        category: AnimationCategory(mainHand));
                    if (TpAnimationBehavior == null) AnimationBehavior?.PlayVanillaAnimation(tpAttackAnimation, mainHand);

                    if (mainHand)
                    {
                        MainHandAttackCounter++;
                    }
                    else
                    {
                        OffHandAttackCounter++;
                    }
                    HandleHitTerrain = false;
                }
                break;
            case MeleeWeaponState.WindingUp:
                break;
            case MeleeWeaponState.Cooldown:
                break;
            case MeleeWeaponState.Attacking:
                break;
            default:
                return false;
        }

        return true;
    }
    protected virtual void TryAttack(MeleeAttack attack, MeleeAttack? handle, StanceStats stats, ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        ItemStackMeleeWeaponStats stackStats = ItemStackMeleeWeaponStats.FromItemStack(slot.Itemstack);

        if (handle != null)
        {
            handle.Attack(
                        player.Player,
                        slot,
                        mainHand,
                        out IEnumerable<(Block block, Vector3d point)> handleTerrainCollision,
                        out IEnumerable<(Vintagestory.API.Common.Entities.Entity entity, Vector3d point)> handleEntitiesCollision,
                        stackStats);

            if (!HandleHitTerrain && handleTerrainCollision.Any())
            {
                if (stats.HandleHitSound != null) SoundsSystem.Play(stats.HandleHitSound);
                HandleHitTerrain = true;
            }

            if (handleTerrainCollision.Any()) return;
        }

        attack.Attack(
            player.Player,
            slot,
            mainHand,
            out IEnumerable<(Block block, Vector3d point)> terrainCollision,
            out IEnumerable<(Vintagestory.API.Common.Entities.Entity entity, Vector3d point)> entitiesCollision,
            stackStats);

        if (handle != null) handle.AddAttackedEntities(attack);

        if (entitiesCollision.Any() && stats.AttackHitSound != null)
        {
            SoundsSystem.Play(stats.AttackHitSound);
        }

        if (entitiesCollision.Any() && Stats.AnimationStaggerOnHitDurationMs > 0)
        {
            AnimationBehavior?.SetSpeedModifier(AttackImpactFunction);
            Api.World.AddCameraShake(Stats.ScreenShakeStrength);
        }
    }
    protected virtual bool AttackImpactFunction(TimeSpan duration, ref TimeSpan delta)
    {
        TimeSpan totalDuration = TimeSpan.FromMilliseconds(Stats.AnimationStaggerOnHitDurationMs);

        /*double multiplier = duration / totalDuration;
        multiplier = Math.Pow(multiplier, 3);
        delta = delta * multiplier;*/

        delta = TimeSpan.Zero;

        return duration < totalDuration;
    }
    protected virtual bool AttackAnimationCallback(bool mainHand)
    {
        AnimationBehavior?.PlayReadyAnimation(mainHand);
        TpAnimationBehavior?.PlayReadyAnimation(mainHand);
        SetState(MeleeWeaponState.Idle, mainHand);

        if (mainHand)
        {
            MainHandAttackCounter = 0;
        }
        else
        {
            OffHandAttackCounter = 0;
        }

        return true;
    }
    protected virtual void AttackAnimationCallbackHandler(string callbackCode, bool mainHand)
    {
        switch (callbackCode)
        {
            case "start":
                SetState(MeleeWeaponState.Attacking, mainHand);
                break;
            case "stop":
                SetState(MeleeWeaponState.Cooldown, mainHand);
                break;
            case "ready":
                SetState(MeleeWeaponState.Idle, mainHand);
                break;
        }
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Released)]
    protected virtual bool StopAttack(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        return false;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Block(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        bool handleEvent = !Settings.DoVanillaActionsWhileBlocking;

        if (eventData.AltPressed) return false;
        if (!CanBlock(mainHand) && !CanParry(mainHand)) return false;
        if (!CheckState(mainHand, MeleeWeaponState.Idle, MeleeWeaponState.WindingUp, MeleeWeaponState.Cooldown)) return handleEvent;
        if (IsBlockOnCooldown(mainHand)) return handleEvent;
        EnsureStance(player, mainHand);
        if (mainHand && CanBlockWithOtherHand(player, mainHand)) return handleEvent;
        if (ActionRestricted(player, mainHand)) return handleEvent;

        StanceStats? stats = GetStanceStats(mainHand);
        DamageBlockJson? parryStats = stats?.Parry?.Clone();
        DamageBlockJson? blockStats = stats?.Block?.Clone();
        ItemStackMeleeWeaponStats stackStats = ItemStackMeleeWeaponStats.FromItemStack(slot.Itemstack);

        if (parryStats?.BlockTier != null)
        {
            foreach ((string damageType, float tier) in parryStats.BlockTier)
            {
                parryStats.BlockTier[damageType] += stackStats.ParryTierBonus;
            }
        }

        if (blockStats?.BlockTier != null)
        {
            foreach ((string damageType, float tier) in blockStats.BlockTier)
            {
                blockStats.BlockTier[damageType] += stackStats.BlockTierBonus;
            }
        }

        if (CanParry(mainHand) && parryStats != null && stats != null)
        {
            if (!ParryButtonReleased) return handleEvent;

            SetState(MeleeWeaponState.Parrying, mainHand);
            AnimationBehavior?.Play(
                mainHand,
                stats.BlockAnimation,
                animationSpeed: PlayerBehavior?.ManipulationSpeed ?? 1,
                category: AnimationCategory(mainHand),
                callback: () => BlockAnimationCallback(mainHand, player),
                callbackHandler: code => BlockAnimationCallbackHandler(code, mainHand, blockStats, parryStats));
            TpAnimationBehavior?.Play(
                mainHand,
                stats.BlockAnimation,
                animationSpeed: PlayerBehavior?.ManipulationSpeed ?? 1,
                category: AnimationCategory(mainHand));
            if (TpAnimationBehavior == null) AnimationBehavior?.PlayVanillaAnimation(stats.BlockTpAnimation, mainHand);

            ParryButtonReleased = false;
        }
        else if (CanBlock(mainHand) && blockStats != null && stats != null)
        {
            SetState(MeleeWeaponState.Blocking, mainHand);
            MeleeBlockSystem.StartBlock(blockStats, mainHand);
            AnimationBehavior?.Play(
                mainHand,
                stats.BlockAnimation,
                animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat),
                category: AnimationCategory(mainHand),
                callback: () => BlockAnimationCallback(mainHand, player),
                callbackHandler: code => BlockAnimationCallbackHandler(code, mainHand, blockStats, parryStats));
            TpAnimationBehavior?.Play(
                mainHand,
                stats.BlockAnimation,
                animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat),
                category: AnimationCategory(mainHand));
            if (TpAnimationBehavior == null) AnimationBehavior?.PlayVanillaAnimation(stats.BlockTpAnimation, mainHand);
        }

        SetSpeedPenalty(mainHand, player);

        return handleEvent;
    }
    protected virtual void BlockAnimationCallbackHandler(string callbackCode, bool mainHand, DamageBlockJson? blockStats, DamageBlockJson? parryStats)
    {
        switch (callbackCode)
        {
            case "startParry":
                {
                    if (CanParry(mainHand) && parryStats != null)
                    {
                        SetState(MeleeWeaponState.Parrying, mainHand);
                        MeleeBlockSystem.StartBlock(parryStats, mainHand);
                    }
                }
                break;
            case "stopParry":
                {
                    if (CanBlock(mainHand) && blockStats != null)
                    {
                        SetState(MeleeWeaponState.Blocking, mainHand);
                        MeleeBlockSystem.StartBlock(blockStats, mainHand);
                    }
                    else
                    {
                        MeleeBlockSystem.StopBlock(mainHand);
                    }
                }
                break;
        }
    }
    protected virtual bool BlockAnimationCallback(bool mainHand, EntityPlayer player)
    {
        if (!CheckState(mainHand, MeleeWeaponState.Parrying)) return true;

        SetState(MeleeWeaponState.Idle, mainHand);
        AnimationBehavior?.PlayReadyAnimation(mainHand);
        TpAnimationBehavior?.PlayReadyAnimation(mainHand);

        float cooldown = GetStanceStats(mainHand)?.BlockCooldownMs ?? 0;
        if (cooldown != 0)
        {
            StartBlockCooldown(mainHand, TimeSpan.FromMilliseconds(cooldown));
        }

        SetSpeedPenalty(mainHand, player);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    protected virtual bool StopBlock(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        ParryButtonReleased = true;
        if (!CheckState(mainHand, MeleeWeaponState.Blocking, MeleeWeaponState.Parrying)) return false;

        MeleeBlockSystem.StopBlock(mainHand);
        AnimationBehavior?.PlayReadyAnimation(mainHand);
        TpAnimationBehavior?.PlayReadyAnimation(mainHand);
        AnimationBehavior?.StopVanillaAnimation(GetStanceStats(mainHand)?.BlockTpAnimation ?? "", mainHand);
        SetState(MeleeWeaponState.Idle, mainHand);

        float cooldown = GetStanceStats(mainHand)?.BlockCooldownMs ?? 0;
        if (cooldown != 0)
        {
            StartBlockCooldown(mainHand, TimeSpan.FromMilliseconds(cooldown));
        }

        SetSpeedPenalty(mainHand, player);

        return true;
    }

    protected virtual void EnsureStance(EntityPlayer player, bool mainHand)
    {
        MeleeWeaponStance currentStance = GetStance<MeleeWeaponStance>(mainHand);

        if (!mainHand)
        {
            SetStance(MeleeWeaponStance.OffHand, mainHand);
            if (currentStance != MeleeWeaponStance.OffHand)
            {
                AnimationBehavior?.PlayReadyAnimation(mainHand);
                TpAnimationBehavior?.PlayReadyAnimation(mainHand);
            }
            return;
        }

        bool offHandEmpty = CheckForOtherHandEmptyNoError(mainHand, player);
        if (offHandEmpty && Stats.TwoHandedStance != null)
        {
            SetStance(MeleeWeaponStance.TwoHanded, mainHand);
            if (currentStance != MeleeWeaponStance.TwoHanded)
            {
                AnimationBehavior?.PlayReadyAnimation(mainHand);
                TpAnimationBehavior?.PlayReadyAnimation(mainHand);
            }
            DirectionsType = Enum.Parse<DirectionsConfiguration>(Stats.TwoHandedStance.AttackDirectionsType);
        }
        else
        {
            SetStance(MeleeWeaponStance.MainHand, mainHand);
            if (currentStance != MeleeWeaponStance.MainHand)
            {
                AnimationBehavior?.PlayReadyAnimation(mainHand);
                TpAnimationBehavior?.PlayReadyAnimation(mainHand);
            }
            if (Stats.OneHandedStance != null) DirectionsType = Enum.Parse<DirectionsConfiguration>(Stats.OneHandedStance.AttackDirectionsType);
        }
    }


    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Aim(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (eventData.AltPressed || !CanThrow(mainHand) || Stats.ThrowAttack == null || AimingStats == null) return false;
        if (!CheckState(mainHand, MeleeWeaponState.Idle)) return false;
        //if (mainHand && CanBlockWithOtherHand(player, mainHand)) return false;

        SetState(MeleeWeaponState.StartingAim, mainHand);
        AimingSystem.AimingState = WeaponAimingState.Blocked;
        AimingAnimationController?.Play(mainHand);

        ItemStackMeleeWeaponStats stackStats = ItemStackMeleeWeaponStats.FromItemStack(slot.Itemstack);
        AimingStats stats = AimingStats.Clone();
        stats.AimDifficulty *= stackStats.ThrownAimingDifficulty;

        AimingSystem.StartAiming(AimingStats);

        AnimationBehavior?.Play(mainHand, Stats.ThrowAttack.AimAnimation, animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat), callback: () => AimAnimationCallback(slot, mainHand));
        TpAnimationBehavior?.Play(mainHand, Stats.ThrowAttack.AimAnimation, animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat));
        if (TpAnimationBehavior == null) AnimationBehavior?.PlayVanillaAnimation(Stats.ThrowAttack.TpAimAnimation, mainHand);

        return true;
    }
    protected virtual bool AimAnimationCallback(ItemSlot slot, bool mainHand)
    {
        SetState(MeleeWeaponState.Aiming, mainHand);
        AimingSystem.AimingState = WeaponAimingState.FullCharge;

        return true;
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Pressed)]
    protected virtual bool Throw(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (Stats.ThrowAttack == null) return false;
        if (!CheckState(mainHand, MeleeWeaponState.Aiming)) return false;

        SetState(MeleeWeaponState.Throwing, mainHand);
        AnimationBehavior?.Play(mainHand, Stats.ThrowAttack.ThrowAnimation, animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat), callback: () => ThrowAnimationCallback(slot, player, mainHand));
        TpAnimationBehavior?.Play(mainHand, Stats.ThrowAttack.ThrowAnimation, animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat));
        AnimationBehavior?.StopVanillaAnimation(Stats.ThrowAttack.TpAimAnimation, mainHand);
        if (TpAnimationBehavior == null) AnimationBehavior?.PlayVanillaAnimation(Stats.ThrowAttack.TpThrowAnimation, mainHand);

        return true;
    }
    protected virtual bool ThrowAnimationCallback(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        if (Stats.ThrowAttack == null) return false;

        SetState(MeleeWeaponState.Idle, mainHand);
        AimingSystem.AimingState = WeaponAimingState.FullCharge;

        Vintagestory.API.MathTools.Vec3d position = player.LocalEyePos + player.Pos.XYZ;
        Vector3 targetDirection = AimingSystem.TargetVec;

        targetDirection = ClientAimingSystem.Zeroing(targetDirection, Stats.ThrowAttack.Zeroing);

        RangedWeaponSystem.Shoot(slot, 1, new Vector3((float)position.X, (float)position.Y, (float)position.Z), new Vector3(targetDirection.X, targetDirection.Y, targetDirection.Z), mainHand, _ => { });

        slot.TakeOut(1);

        Api.World.AddCameraShake(Stats.ThrowScreenShakeStrength);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    protected virtual bool Ease(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!CheckState(mainHand, MeleeWeaponState.StartingAim, MeleeWeaponState.Aiming)) return false;

        AnimationBehavior?.PlayReadyAnimation(mainHand);
        TpAnimationBehavior?.PlayReadyAnimation(mainHand);
        SetState(MeleeWeaponState.Idle, mainHand);
        AimingAnimationController?.Stop(mainHand);
        AimingSystem.StopAiming();
        AnimationBehavior?.StopVanillaAnimation(Stats.ThrowAttack?.TpAimAnimation ?? "", mainHand);

        return true;
    }


    protected virtual void StartAttackCooldown(bool mainHand, TimeSpan time)
    {
        StopAttackCooldown(mainHand);

        if (mainHand)
        {
            MainHandAttackCooldownTimer = Api.World.RegisterCallback(_ => MainHandAttackCooldownTimer = -1, (int)(time.TotalMilliseconds / PlayerBehavior?.ManipulationSpeed ?? 1));
        }
        else
        {
            OffHandAttackCooldownTimer = Api.World.RegisterCallback(_ => OffHandAttackCooldownTimer = -1, (int)(time.TotalMilliseconds / PlayerBehavior?.ManipulationSpeed ?? 1));
        }
    }
    protected virtual void StopAttackCooldown(bool mainHand)
    {
        if (mainHand)
        {
            if (MainHandAttackCooldownTimer != -1)
            {
                Api.World.UnregisterCallback(MainHandAttackCooldownTimer);
                MainHandAttackCooldownTimer = -1;
            }
        }
        else
        {
            if (OffHandAttackCooldownTimer != -1)
            {
                Api.World.UnregisterCallback(OffHandAttackCooldownTimer);
                OffHandAttackCooldownTimer = -1;
            }
        }
    }
    protected virtual bool IsAttackOnCooldown(bool mainHand) => mainHand ? MainHandAttackCooldownTimer != -1 : OffHandAttackCooldownTimer != -1;

    protected virtual void StartBlockCooldown(bool mainHand, TimeSpan time)
    {
        StopBlockCooldown(mainHand);

        if (mainHand)
        {
            MainHandBlockCooldownTimer = Api.World.RegisterCallback(_ => MainHandBlockCooldownTimer = -1, (int)(time.TotalMilliseconds / PlayerBehavior?.ManipulationSpeed ?? 1));
        }
        else
        {
            OffHandBlockCooldownTimer = Api.World.RegisterCallback(_ => OffHandBlockCooldownTimer = -1, (int)(time.TotalMilliseconds / PlayerBehavior?.ManipulationSpeed ?? 1));
        }
    }
    protected virtual void StopBlockCooldown(bool mainHand)
    {
        if (mainHand)
        {
            if (MainHandBlockCooldownTimer != -1)
            {
                Api.World.UnregisterCallback(MainHandBlockCooldownTimer);
                MainHandBlockCooldownTimer = -1;
            }
        }
        else
        {
            if (OffHandBlockCooldownTimer != -1)
            {
                Api.World.UnregisterCallback(OffHandBlockCooldownTimer);
                OffHandBlockCooldownTimer = -1;
            }
        }
    }
    protected virtual bool IsBlockOnCooldown(bool mainHand) => mainHand ? MainHandBlockCooldownTimer != -1 : OffHandBlockCooldownTimer != -1;

    protected static string AnimationCategory(bool mainHand = true) => mainHand ? "main" : "mainOffhand";

    protected MeleeAttack? GetStanceAttack(bool mainHand = true, AttackDirection direction = AttackDirection.Top)
    {
        MeleeWeaponStance stance = GetStance<MeleeWeaponStance>(mainHand);
        return stance switch
        {
            MeleeWeaponStance.MainHand => OneHandedAttack ?? DirectionalOneHandedAttacks?.GetValueOrDefault(direction),
            MeleeWeaponStance.OffHand => OffHandAttack ?? DirectionalOffHandAttacks?.GetValueOrDefault(direction),
            MeleeWeaponStance.TwoHanded => TwoHandedAttack ?? DirectionalTwoHandedAttacks?.GetValueOrDefault(direction),
            _ => OneHandedAttack,
        };
    }
    protected MeleeAttack? GetStanceHandleAttack(bool mainHand = true)
    {
        MeleeWeaponStance stance = GetStance<MeleeWeaponStance>(mainHand);
        return stance switch
        {
            MeleeWeaponStance.MainHand => OneHandedHandleAttack,
            MeleeWeaponStance.OffHand => OffHandHandleAttack,
            MeleeWeaponStance.TwoHanded => TwoHandedHandleAttack,
            _ => OneHandedHandleAttack,
        };
    }
    protected StanceStats? GetStanceStats(bool mainHand = true)
    {
        MeleeWeaponStance stance = GetStance<MeleeWeaponStance>(mainHand);
        return stance switch
        {
            MeleeWeaponStance.MainHand => Stats.OneHandedStance,
            MeleeWeaponStance.OffHand => Stats.OffHandStance,
            MeleeWeaponStance.TwoHanded => Stats.TwoHandedStance,
            _ => Stats.OneHandedStance,
        };
    }
    protected static bool CheckState<TState>(int state, params TState[] statesToCheck)
        where TState : struct, Enum
    {
        return statesToCheck.Contains((TState)Enum.ToObject(typeof(TState), state % _maxStates));
    }
    protected bool CheckState<TState>(bool mainHand, params TState[] statesToCheck)
        where TState : struct, Enum
    {
        return statesToCheck.Contains((TState)Enum.ToObject(typeof(TState), PlayerBehavior?.GetState(mainHand) % _maxStates ?? 0));
    }
    protected void SetStance<TStance>(TStance stance, bool mainHand = true)
        where TStance : struct, Enum
    {
        int stateCombined = PlayerBehavior?.GetState(mainHand) ?? 0;
        int stateInt = stateCombined % _maxStates;
        int stanceInt = (int)Enum.ToObject(typeof(TStance), stance);
        stateCombined = stateInt + _maxStates * stanceInt;

        PlayerBehavior?.SetState(stateCombined, mainHand);
    }
    protected void SetState<TState>(TState state, bool mainHand = true)
        where TState : struct, Enum
    {
        int stateCombined = PlayerBehavior?.GetState(mainHand) ?? 0;
        int stanceInt = stateCombined / _maxStates;
        int stateInt = (int)Enum.ToObject(typeof(TState), state);
        stateCombined = stateInt + _maxStates * stanceInt;

        PlayerBehavior?.SetState(stateCombined, mainHand);
    }
    protected void SetStateAndStance<TState, TStance>(TState state, TStance stance, bool mainHand = true)
        where TState : struct, Enum
        where TStance : struct, Enum
    {
        int stateInt = (int)Enum.ToObject(typeof(TState), state);
        int stanceInt = (int)Enum.ToObject(typeof(TStance), stance);
        int stateCombined = stateInt + _maxStates * stanceInt;

        PlayerBehavior?.SetState(stateCombined, mainHand);
    }
    protected TState GetState<TState>(bool mainHand = true)
        where TState : struct, Enum
    {
        return (TState)Enum.ToObject(typeof(TState), PlayerBehavior?.GetState(mainHand) % _maxStates ?? 0);
    }
    protected TStance GetStance<TStance>(bool mainHand = true)
        where TStance : struct, Enum
    {
        return (TStance)Enum.ToObject(typeof(TStance), PlayerBehavior?.GetState(mainHand) / _maxStates ?? 0);
    }
    protected bool CanAttackWithOtherHand(EntityPlayer player, bool mainHand = true)
    {
        ItemSlot otherHandSlot = mainHand ? player.LeftHandItemSlot : player.RightHandItemSlot;
        return (otherHandSlot.Itemstack?.Item as IHasMeleeWeaponActions)?.CanAttack(!mainHand) ?? false;
    }
    protected bool CanBlockWithOtherHand(EntityPlayer player, bool mainHand = true)
    {
        ItemSlot otherHandSlot = mainHand ? player.LeftHandItemSlot : player.RightHandItemSlot;
        return (otherHandSlot.Itemstack?.Item as IHasMeleeWeaponActions)?.CanBlock(!mainHand) ?? false;
    }

    protected static bool ActionRestricted(EntityPlayer player, bool mainHand = true)
    {
        if (mainHand)
        {
            return (player.LeftHandItemSlot.Itemstack?.Item as IRestrictAction)?.RestrictRightHandAction() ?? false;
        }
        else
        {
            return (player.RightHandItemSlot.Itemstack?.Item as IRestrictAction)?.RestrictLeftHandAction() ?? false;
        }
    }

    protected bool CheckForOtherHandEmpty(bool mainHand, EntityPlayer player)
    {
        if (mainHand && !player.LeftHandItemSlot.Empty)
        {
            (player.World.Api as ICoreClientAPI)?.TriggerIngameError(this, "offhandShouldBeEmpty", Lang.Get("Offhand should be empty"));
            return false;
        }

        if (!mainHand && !player.RightHandItemSlot.Empty)
        {
            (player.World.Api as ICoreClientAPI)?.TriggerIngameError(this, "mainHandShouldBeEmpty", Lang.Get("Main hand should be empty"));
            return false;
        }

        return true;
    }
    protected bool CheckForOtherHandEmptyNoError(bool mainHand, EntityPlayer player)
    {
        if (mainHand && !player.LeftHandItemSlot.Empty)
        {
            return false;
        }

        if (!mainHand && !player.RightHandItemSlot.Empty)
        {
            return false;
        }

        return true;
    }

    protected void SetSpeedPenalty(bool mainHand, EntityPlayer player)
    {
        if (HasSpeedPenalty(mainHand, out float penalty))
        {
            PlayerBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory, penalty);
        }
        else
        {
            PlayerBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory);
        }
    }
    protected bool HasSpeedPenalty(bool mainHand, out float penalty)
    {
        penalty = 0;

        StanceStats? stance = GetStanceStats(mainHand);

        if (stance == null) return false;

        if (CheckState(mainHand, MeleeWeaponState.Blocking, MeleeWeaponState.Parrying))
        {
            penalty = stance.BlockSpeedPenalty;
        }
        else
        {
            penalty = stance.SpeedPenalty;
        }

        return MathF.Abs(penalty) > 1E-9f; // just some epsilon
    }
    protected float GetAnimationSpeed(Entity player, string proficiencyStat, float min = 0.5f, float max = 2f)
    {
        float manipulationSpeed = PlayerBehavior?.ManipulationSpeed ?? 1;
        float proficiencyBonus = proficiencyStat == "" ? 0 : player.Stats.GetBlended(proficiencyStat) - 1;
        return Math.Clamp(manipulationSpeed + proficiencyBonus, min, max);
    }

    protected static void RegisterCollider(string item, string type, MeleeAttack attack)
    {
#if DEBUG
        int typeIndex = 0;
        foreach (MeleeDamageType damageType in attack.DamageTypes)
        {
            DebugWindowManager.RegisterCollider(item, type + typeIndex++, damageType);
        }
#endif
    }

    protected static string GetAttackStatsDescription(ItemSlot inSlot, IEnumerable<DamageDataJson> damageTypesData, string descriptionLangCode)
    {
        float damage = 0;
        float tier = 0;
        HashSet<string> damageTypes = new();

        ItemStackMeleeWeaponStats stackStats = ItemStackMeleeWeaponStats.FromItemStack(inSlot.Itemstack);

        foreach (DamageDataJson attack in damageTypesData)
        {
            float attackDamage = attack.Damage * stackStats.DamageMultiplier;

            if (attackDamage > damage)
            {
                damage = attackDamage;
            }
            float currentTier = Math.Max(attack.Strength, attack.Tier) + stackStats.DamageTierBonus;
            if (currentTier > tier)
            {
                tier = currentTier;
            }

            damageTypes.Add(attack.DamageType);
        }

        string damageType = damageTypes.Select(element => Lang.Get($"combatoverhaul:damage-type-{element}")).Aggregate((first, second) => $"{first}, {second}");

        return Lang.Get(descriptionLangCode, $"{damage:F1}", tier, damageType);
    }
}

public class MeleeWeaponServer : RangeWeaponServer
{
    public MeleeWeaponServer(ICoreServerAPI api, Item item) : base(api, item)
    {
        ProjectileSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().ServerProjectileSystem ?? throw new Exception();
        try
        {
            Stats = item.Attributes.AsObject<MeleeWeaponStats>();
        }
        catch (Exception exception)
        {
            throw new Exception($"Error while getting stats for item '{item.Code}' on server side: {exception.Message}");
        }
    }

    public override bool Shoot(IServerPlayer player, ItemSlot slot, ShotPacket packet, Entity shooter)
    {
        if (slot?.Itemstack == null || slot.Itemstack.StackSize < 1) return false;

        ProjectileStats? stats = slot.Itemstack.Item.GetCollectibleBehavior<ProjectileBehavior>(true)?.GetStats(slot.Itemstack);
        ItemStackMeleeWeaponStats stackStats = ItemStackMeleeWeaponStats.FromItemStack(slot.Itemstack);

        if (stats == null)
        {
            return false;
        }

        ProjectileSpawnStats spawnStats = new()
        {
            ProducerEntityId = player.Entity.EntityId,
            DamageMultiplier = 1 * stackStats.ThrownDamageMultiplier,
            DamageStrength = Stats.ThrowAttack?.DamageStrength ?? 0 + stackStats.ThrownDamageTierBonus,
            Position = new Vector3d(packet.Position[0], packet.Position[1], packet.Position[2]),
            Velocity = Vector3d.Normalize(new Vector3d(packet.Velocity[0], packet.Velocity[1], packet.Velocity[2])) * (Stats.ThrowAttack?.Velocity ?? 1) * stackStats.ThrownProjectileSpeedMultiplier
        };

        AssetLocation projectileCode = slot.Itemstack.Item.Code.Clone();

        ProjectileSystem.Spawn(packet.ProjectileId[0], stats, spawnStats, slot.TakeOut(1), slot.Itemstack, shooter);

        SwapToNewProjectile(player, slot, projectileCode);

        slot.MarkDirty();

        return true;
    }

    protected readonly MeleeWeaponStats Stats;

    protected static void SwapToNewProjectile(IServerPlayer player, ItemSlot slot, AssetLocation projectileCode)
    {
        if (slot.Itemstack == null || slot.Itemstack.StackSize == 0)
        {
            ItemSlot? replacementSlot = null;
            WalkInventory(player.Entity, slot =>
            {
                if (slot?.Itemstack?.Item?.Code == null) return true;

                if (slot.Itemstack.Item.Code.ToString() == projectileCode.ToString())
                {
                    replacementSlot = slot;
                    return false;
                }

                return true;
            });

            if (replacementSlot == null)
            {
                string projectilePath = projectileCode.ToShortString();

                while (projectilePath.Contains('-'))
                {
                    int delimiterIndex = projectilePath.LastIndexOf('-');
                    projectilePath = projectilePath.Substring(0, delimiterIndex);
                    string wildcard = $"{projectilePath}-*";

                    WalkInventory(player.Entity, slot =>
                    {
                        if (slot?.Itemstack?.Item?.Code == null) return true;

                        if (WildcardUtil.Match(wildcard, slot.Itemstack.Item.Code.ToString()))
                        {
                            replacementSlot = slot;
                            return false;
                        }

                        return true;
                    });

                    if (replacementSlot != null) break;
                }
            }

            if (replacementSlot != null)
            {
                slot.TryFlipWith(replacementSlot);
                replacementSlot.MarkDirty();
            }
        }
    }

    protected static void WalkInventory(EntityPlayer player, System.Func<ItemSlot, bool> selector)
    {
        foreach (ItemSlot hotbarSlot in player.Player.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName))
        {
            if (!selector.Invoke(hotbarSlot))
            {
                return;
            }
        }

        foreach (ItemSlot backpackSlot in player.Player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName))
        {
            if (!selector.Invoke(backpackSlot))
            {
                return;
            }
        }
    }
}

public interface IRestrictAction
{
    bool RestrictRightHandAction();
    bool RestrictLeftHandAction();
}

public class MeleeWeapon : Item, IHasWeaponLogic, IHasRangedWeaponLogic, IHasDynamicIdleAnimations, IHasMeleeWeaponActions, IHasServerBlockCallback, ISetsRenderingOffset, IMouseWheelInput, IOnGameTick, IRestrictAction
{
    public MeleeWeaponClient? ClientLogic { get; private set; }
    public MeleeWeaponServer? ServerLogic { get; private set; }

    IClientWeaponLogic? IHasWeaponLogic.ClientLogic => ClientLogic;
    IServerRangedWeaponLogic? IHasRangedWeaponLogic.ServerWeaponLogic => ServerLogic;

    public bool RenderingOffset { get; set; }

    public bool RestrictRightHandAction() => ClientLogic?.RestrictRightHandAction() ?? false;
    public bool RestrictLeftHandAction() => ClientLogic?.RestrictLeftHandAction() ?? false;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is ICoreClientAPI clientAPI)
        {
            ClientLogic = new(clientAPI, this);
            MeleeWeaponStats Stats = Attributes.AsObject<MeleeWeaponStats>();
            RenderingOffset = Stats.RenderingOffset;

            if (Stats.OneHandedStance?.GripMinLength != Stats.OneHandedStance?.GripMaxLength || Stats.TwoHandedStance?.GripMinLength != Stats.TwoHandedStance?.GripMaxLength)
            {
                ChangeGripInteraction = new()
                {
                    MouseButton = EnumMouseButton.Wheel,
                    ActionLangCode = "combatoverhaul:interaction-grip-change"
                };
            }

        }

        if (api is ICoreServerAPI serverAPI)
        {
            ServerLogic = new(serverAPI, this);
        }

        AltForInteractions = new()
        {
            MouseButton = EnumMouseButton.None,
            HotKeyCode = "Alt",
            ActionLangCode = "combatoverhaul:interaction-hold-alt"
        };
    }

    public AnimationRequestByCode? GetIdleAnimation(bool mainHand) => ClientLogic?.GetIdleAnimation(mainHand);
    public AnimationRequestByCode? GetReadyAnimation(bool mainHand) => ClientLogic?.GetReadyAnimation(mainHand);

    public override void OnHeldRenderOpaque(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        base.OnHeldRenderOpaque(inSlot, byPlayer);

        if (DebugWindowManager.RenderDebugColliders)
        {
            ClientLogic?.RenderDebugCollider(inSlot, byPlayer);
        }
    }

    public bool CanAttack(bool mainHand) => ClientLogic?.CanAttack(mainHand) ?? false;
    public bool CanBlock(bool mainHand) => (ClientLogic?.CanBlock(mainHand) ?? false) || (ClientLogic?.CanParry(mainHand) ?? false);
    public void OnGameTick(ItemSlot slot, EntityPlayer player, ref int state, bool mainHand)
    {
        ClientLogic?.OnGameTick(slot, player, ref state, mainHand);
    }

    public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
    {
        handling = EnumHandHandling.PreventDefaultAction;
    }
    public override float OnBlockBreaking(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter)
    {
        return remainingResistance;
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        ClientLogic?.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        dsc.AppendLine("");

        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
    }

    public override WorldInteraction?[]? GetHeldInteractionHelp(ItemSlot inSlot)
    {
        WorldInteraction?[]? interactionHelp = base.GetHeldInteractionHelp(inSlot);

        if (ChangeGripInteraction != null)
        {
            interactionHelp = interactionHelp?.Append(ChangeGripInteraction);
        }

        return interactionHelp?.Append(AltForInteractions);
    }

    public void BlockCallback(IServerPlayer player, ItemSlot slot, bool mainHand, float damageBlocked)
    {
        DamageItem(player.Entity.World, player.Entity, slot, 1);
        //DamageItem(player.Entity.World, player.Entity, slot, (int)MathF.Ceiling(damageBlocked)); // Damages swords too much
    }

    public bool OnMouseWheel(ItemSlot slot, IClientPlayer byPlayer, float delta) => ClientLogic?.OnMouseWheel(slot, byPlayer, delta) ?? false;

    protected WorldInteraction? AltForInteractions;
    protected WorldInteraction? ChangeGripInteraction;
}

public sealed class GripController
{
    public GripController(FirstPersonAnimationsBehavior? animationBehavior)
    {
        _animationBehavior = animationBehavior;
    }


    public void ChangeGrip(float delta, bool mainHand, float gripFactor, float min, float max)
    {
        _grip = GameMath.Clamp(_grip + delta * gripFactor, min, max);

        PlayAnimation(mainHand);

    }
    public void ResetGrip(bool mainHand)
    {
        _grip = 0;

        _animationBehavior?.Stop("grip");
    }

    private float _grip = 0;
    private readonly Animations.Animation _gripAnimation = Animations.Animation.Zero.Clone();
    private readonly FirstPersonAnimationsBehavior? _animationBehavior;

    private PLayerKeyFrame GetAimingFrame()
    {
        AnimationElement element = new(_grip, null, null, null, null, null);
        AnimationElement nullElement = new(null, null, null, null, null, null);

        PlayerFrame frame = new(rightHand: new(element, nullElement, nullElement));

        return new PLayerKeyFrame(frame, TimeSpan.Zero, EasingFunctionType.Linear);
    }
    private void PlayAnimation(bool mainHand)
    {
        _gripAnimation.PlayerKeyFrames[0] = GetAimingFrame();
        _gripAnimation.Hold = true;

        AnimationRequest request = new(_gripAnimation, 1.0f, 0, "grip", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), true);
        _animationBehavior?.Play(request, mainHand);
    }
}
