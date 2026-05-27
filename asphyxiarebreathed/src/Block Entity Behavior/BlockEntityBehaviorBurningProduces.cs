using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Vintagestory.API;
using Vintagestory.API.MathTools;
using Vintagestory.API.Datastructures;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Server;

namespace AsphyxiaRebreathed
{
    public class BlockEntityBehaviorBurningProduces : BlockEntityBehavior
    {
        GasSystem gasHandler;
        public Dictionary<string, float> produceGas;
        public Dictionary<string, float> smolderGas;
        public bool smolderWhenLit;
        BlockPos blockPos
        {
            get { return Blockentity.Pos; }
        }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);
            gasHandler = api.ModLoader.GetModSystem<GasSystem>();
            Blockentity.RegisterGameTickListener(ProduceCO, 5000);
            produceGas = GasSourceProperties.GetProduceGas(properties, Blockentity.Block);
            smolderGas = GasSourceProperties.GetSmolderGas(properties, Blockentity.Block);
            smolderWhenLit = properties["smolderWhenLit"].AsBool(false);
            if (produceGas == null || produceGas.Count < 1)
            {
                produceGas = new Dictionary<string, float>();
                produceGas.Add("carbonmonoxide", 0.2f);
                produceGas.Add("carbondioxide", 0.5f);
            }
        }

        public void ProduceCO(float dt)
        {
            if (Api.Side != EnumAppSide.Server) return;

            if (IsBurning())
            {
                if (GasConfig.Loaded.Explosions && gasHandler.ShouldExplode(blockPos))
                {
                    (Api.World as IServerWorldAccessor).CreateExplosion(blockPos, EnumBlastType.RockBlast, 3, 3);
                }
                else if (GasConfig.Loaded.Smoke)
                {
                    Dictionary<string, float> gases = smolderWhenLit && HasSmolderGas() ? smolderGas : produceGas;
                    gasHandler.QueueGasExchange(new Dictionary<string, float>(gases), blockPos);
                }
            }
            else if (GasConfig.Loaded.Smoke && HasSmolderGas() && !smolderWhenLit)
            {
                gasHandler.QueueGasExchange(new Dictionary<string, float>(smolderGas), blockPos);
            }
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);

            if (!GasConfig.Loaded.GasesDebugEnabled) return;

            GasDebugInfo.AppendGasList(dsc, "Burning Produces:", produceGas);
            GasDebugInfo.AppendGasList(dsc, "Smolder Produces:", smolderGas);
        }

        private bool HasSmolderGas()
        {
            return smolderGas != null && smolderGas.Count > 0;
        }

        private bool IsBurning()
        {
            return (Blockentity as BlockEntityFirepit)?.IsBurning == true ||
                (Blockentity as BlockEntityForge)?.IsBurning == true ||
                (Blockentity as BlockEntityBloomery)?.IsBurning == true ||
                (Blockentity as BlockEntityCoalPile)?.IsBurning == true ||
                ((Blockentity as BlockEntityTorch)?.Block.LightHsv[2] ?? 0) > 0 ||
                ((Blockentity as BlockEntityTorchHolder)?.Block.LightHsv[2] ?? 0) > 0 ||
                (Blockentity as BlockEntityPitKiln)?.Lit == true ||
                (Blockentity as BlockEntityCharcoalPit)?.Lit == true ||
                (Blockentity as BlockEntityBoiler)?.IsBurning == true ||
                Blockentity.Block.Code.Path == "fire";
        }

        public BlockEntityBehaviorBurningProduces(BlockEntity blockentity) : base(blockentity)
        {
        }
    }
}
