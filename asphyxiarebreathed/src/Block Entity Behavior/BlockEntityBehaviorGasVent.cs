using System.Collections.Generic;
using System.Text;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace AsphyxiaRebreathed
{
    public class BlockEntityBehaviorGasVent : BlockEntityBehavior
    {
        GasSystem gasHandler;
        Dictionary<string, float> produceGas;
        int updateTimeInMS;
        double updateTimeInHours;
        double lastTimeProduced;
        int minY;
        int maxY;
        int maxLight;
        bool requireSkyless;
        bool requireAdjacentAir;
        bool ignoreLiquids;
        bool ignoreSides;

        BlockPos blockPos
        {
            get { return Blockentity.Pos; }
        }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);

            gasHandler = api.ModLoader.GetModSystem<GasSystem>();
            produceGas = GasSourceProperties.GetProduceGas(properties, Blockentity.Block);
            updateTimeInMS = properties["updateMS"].AsInt(10000);
            updateTimeInHours = properties["updateHours"].AsDouble();
            minY = properties["minY"].AsInt(int.MinValue);
            maxY = properties["maxY"].AsInt(int.MaxValue);
            maxLight = properties["maxLight"].AsInt(int.MaxValue);
            requireSkyless = properties["requireSkyless"].AsBool(true);
            requireAdjacentAir = properties["requireAdjacentAir"].AsBool(true);
            ignoreLiquids = properties["ignoreLiquids"].AsBool(false);
            ignoreSides = properties["ignoreSides"].AsBool(false);

            Blockentity.RegisterGameTickListener(ProduceGas, updateTimeInMS);
        }

        public void ProduceGas(float dt)
        {
            if (Blockentity.Api.World.Calendar.TotalHours - lastTimeProduced < updateTimeInHours) return;
            if (Api.Side != EnumAppSide.Server || produceGas == null || produceGas.Count < 1) return;
            if (!CanVent()) return;

            lastTimeProduced = Blockentity.Api.World.Calendar.TotalHours;
            gasHandler.QueueGasExchange(new Dictionary<string, float>(produceGas), blockPos, 0, ignoreLiquids, ignoreSides);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);

            if (!GasConfig.Loaded.GasesDebugEnabled) return;

            GasDebugInfo.AppendGasList(dsc, "Gas Vent Produces:", produceGas);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetDouble("gassyslastVented", lastTimeProduced);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            lastTimeProduced = tree.GetDouble("gassyslastVented");
        }

        private bool CanVent()
        {
            IBlockAccessor blockAccessor = Api.World.BlockAccessor;
            if (blockPos.Y < minY || blockPos.Y > maxY) return false;
            if (requireSkyless && blockAccessor.GetRainMapHeightAt(blockPos) < blockPos.Y) return false;
            if (blockAccessor.GetLightLevel(blockPos, EnumLightLevelType.MaxLight) > maxLight) return false;
            if (!requireAdjacentAir) return true;

            BlockPos checkPos = blockPos.Copy();

            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                checkPos.Set(blockPos);
                checkPos.Add(face);

                if (blockAccessor.GetBlock(checkPos).Replaceable >= 6000) return true;
            }

            return false;
        }

        public BlockEntityBehaviorGasVent(BlockEntity blockentity) : base(blockentity)
        {
        }
    }
}
