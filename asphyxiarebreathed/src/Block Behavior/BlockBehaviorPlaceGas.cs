using Vintagestory.API.Common;
using Vintagestory.API;
using Vintagestory.API.MathTools;
using Vintagestory.API.Datastructures;
using System.Collections.Generic;

namespace AsphyxiaRebreathed
{
    public class BlockBehaviorPlaceGas : BlockBehavior
    {
        public Dictionary<string, float> produceGas;

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            produceGas = GasSourceProperties.GetProduceGas(properties, block);
        }

        public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ref EnumHandling handling)
        {
            base.OnBlockPlaced(world, blockPos, ref handling);

            if (!GasConfig.Loaded.GasesEnabled || world.Side != EnumAppSide.Server || produceGas == null || produceGas.Count < 1) return;

            world.Api.ModLoader.GetModSystem<GasSystem>()?.QueueGasExchange(new Dictionary<string, float>(produceGas), blockPos);
        }

        public BlockBehaviorPlaceGas(Block block) : base(block)
        {
        }
    }
}
