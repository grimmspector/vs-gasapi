using Vintagestory.API.Common;
using Vintagestory.API;
using Vintagestory.API.MathTools;
using Vintagestory.API.Datastructures;
using System.Collections.Generic;

namespace AsphyxiaRebreathed
{
    public class BlockBehaviorMineGas : BlockBehavior
    {
        public Dictionary<string, float> produceGas;
        public bool onRemove = false;
        public bool dryDust = true;

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            produceGas = GasSourceProperties.GetProduceGas(properties, block);
            onRemove = properties.IsTrue("onRemove");
            dryDust = properties["dryDust"].AsBool(true);
        }

        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier, ref EnumHandling handling)
        {
            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier, ref handling);

            if (onRemove || !GasConfig.Loaded.GasesEnabled || world.Side != EnumAppSide.Server || produceGas == null || produceGas.Count < 1) return;

            Dictionary<string, float> gases = GetGasesForConditions(world, pos);
            if (gases.Count < 1) return;

            GasSystem gasSystem = world.Api.ModLoader.GetModSystem<GasSystem>();
            gasSystem?.ReleaseGasSeep(pos);
            gasSystem?.QueueGasExchange(gases, pos);
            gasSystem?.TryCreateGasSeep(world, block, pos, gases);
        }

        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos, ref EnumHandling handling)
        {
            base.OnBlockRemoved(world, pos, ref handling);

            if (!onRemove || !GasConfig.Loaded.GasesEnabled || world.Side != EnumAppSide.Server || produceGas == null || produceGas.Count < 1) return;

            Dictionary<string, float> gases = GetGasesForConditions(world, pos);
            if (gases.Count < 1) return;

            GasSystem gasSystem = world.Api.ModLoader.GetModSystem<GasSystem>();
            gasSystem?.ReleaseGasSeep(pos);
            gasSystem?.QueueGasExchange(gases, pos);
        }

        private Dictionary<string, float> GetGasesForConditions(IWorldAccessor world, BlockPos pos)
        {
            Dictionary<string, float> gases = new Dictionary<string, float>(produceGas);
            if (!dryDust || !IsWet(world, pos)) return gases;

            gases.Remove("coaldust");
            gases.Remove("silicadust");

            return gases;
        }

        private bool IsWet(IWorldAccessor world, BlockPos pos)
        {
            Block above = world.BlockAccessor.GetBlock(pos.UpCopy());
            if (above.IsLiquid()) return true;

            Block fluid = world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Fluid);
            return fluid.IsLiquid();
        }

        public BlockBehaviorMineGas(Block block) : base(block)
        {
        }
    }
}
