using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace AsphyxiaRebreathed
{
    public class BlockBehaviorGas : BlockBehavior
    {
        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            if (!GasConfig.Loaded.GasesDebugEnabled) return null;
            GasSystem gasworks = world.Api.ModLoader.GetModSystem<GasSystem>();
            if (gasworks == null) return null;

            Dictionary<string, float> gasesHere = gasworks.GetGases(pos);
            StringBuilder dsc = new StringBuilder();
            if (!GasDebugInfo.AppendGasList(dsc, "Gases at Position:", gasesHere)) return null;

            return dsc.ToString();
        }

        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos, ref EnumHandling handling)
        {
            base.OnBlockRemoved(world, pos, ref handling);

            if (world.Side != EnumAppSide.Server || block.GetBehavior<BlockBehaviorMineGas>() != null || world.Rand.NextDouble() > GasConfig.Loaded.SpreadGasOnBreakChance) return;

            GasSystem gasHandler = world.Api.ModLoader.GetModSystem<GasSystem>();

            gasHandler.QueueGasExchange(null, pos);
        }

        public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ref EnumHandling handling)
        {
            base.OnBlockPlaced(world, blockPos, ref handling);

            if (world.Side != EnumAppSide.Server || world.Rand.NextDouble() > GasConfig.Loaded.SpreadGasOnPlaceChance) return;

            GasSystem gasHandler = world.Api.ModLoader.GetModSystem<GasSystem>();

            gasHandler.QueueGasExchange(null, blockPos);
        }

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos, ref EnumHandling handling)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos, ref handling);

            if (world.Side == EnumAppSide.Server && world.Rand.NextDouble() <= GasConfig.Loaded.UpdateSpreadGasChance)
            {
                world.Api.ModLoader.GetModSystem<GasSystem>()?.QueueGasExchange(null, pos);
            }
        }

        public BlockBehaviorGas(Block block) : base(block)
        {
        }
    }
}
