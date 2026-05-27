using Vintagestory.API.Common;
using Vintagestory.API;
using Vintagestory.API.MathTools;
using Vintagestory.API.Datastructures;
using System.Collections.Generic;

namespace AsphyxiaRebreathed
{
    public class BlockBehaviorExplosionGas : BlockBehavior
    {
        public Dictionary<string, float> produceGas;

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            produceGas = GasSourceProperties.GetProduceGas(properties, block);
        }

        public override void OnBlockExploded(IWorldAccessor world, BlockPos pos, BlockPos explosionCenter, EnumBlastType blastType, ref EnumHandling handling)
        {
            base.OnBlockExploded(world, pos, explosionCenter, blastType, ref handling);
            
            if (!GasConfig.Loaded.GasesEnabled || produceGas == null || produceGas.Count < 1) return;
            
            world.Api.ModLoader.GetModSystem<GasSystem>()?.AddToExplosion(explosionCenter, produceGas);
        }


        public BlockBehaviorExplosionGas(Block block) : base(block)
        {
        }
    }
}
