using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace AsphyxiaRebreathed
{
    public class GasSystem : ModSystem
    {
        private ICoreServerAPI sapi;
        private Dictionary<BlockPos, Dictionary<string, float>> spreadGasQueue = new Dictionary<BlockPos, Dictionary<string, float>>();
        public static object spreadGasLock = new object();
        private Dictionary<BlockPos, Dictionary<string, float>> ExplosionQueue = new Dictionary<BlockPos, Dictionary<string, float>>();
        private Dictionary<Vec2i, Dictionary<string, double>> PollutionPerChunk = new Dictionary<Vec2i, Dictionary<string, double>>();
        private Dictionary<BlockPos, GasSeepSource> GasSeeps = new Dictionary<BlockPos, GasSeepSource>();
        public int GasSpreadBlockRadius;
        EntityPartitioning entityUtil;

        ICoreAPI api;

        IClientNetworkChannel clientChannel;
        static IServerNetworkChannel serverChannel;

        public static Dictionary<string, GasInfo> GasDictionary = new Dictionary<string, GasInfo>();

        private GasSpreadingThread gasSpreader;

        private Harmony harmony;

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return true;
        }

        public override void StartPre(ICoreAPI api)
        {
            base.StartPre(api);

            try
            {
                GasConfig FromDisk;
                if ((FromDisk = api.LoadModConfig<GasConfig>(GasConfig.ConfigPath)) == null)
                {
                    FromDisk = api.LoadModConfig<GasConfig>(GasConfig.LegacyConfigPath);
                }

                if (FromDisk != null) GasConfig.Loaded = FromDisk;

                api.StoreModConfig<GasConfig>(GasConfig.Loaded, GasConfig.ConfigPath);
            }
            catch
            {
                api.StoreModConfig<GasConfig>(GasConfig.Loaded, GasConfig.ConfigPath);
            }

            api.World.Config.SetBool("ARgasesEnabled", GasConfig.Loaded.GasesEnabled);
            api.World.Config.SetBool("ARLavaVentsEnabled", GasConfig.Loaded.GasesEnabled && GasConfig.Loaded.LavaVents);
        }

        public override void Start(ICoreAPI api)
        {
            this.api = api;

            api.RegisterBlockBehaviorClass("Gas", typeof(BlockBehaviorGas));
            api.RegisterBlockBehaviorClass("SparkGas", typeof(BlockBehaviorSparkGas));
            api.RegisterBlockBehaviorClass("MineGas", typeof(BlockBehaviorMineGas));
            api.RegisterBlockBehaviorClass("ExplosionGas", typeof(BlockBehaviorExplosionGas));
            api.RegisterBlockBehaviorClass("PlaceGas", typeof(BlockBehaviorPlaceGas));

            api.RegisterBlockClass("BlockGas", typeof(BlockGas));

            api.RegisterEntityBehaviorClass("gasinteract", typeof(EntityBehaviorGas));
            api.RegisterEntityBehaviorClass("air", typeof(EntityBehaviorAir));

            api.RegisterBlockEntityBehaviorClass("BurningProduces", typeof(BlockEntityBehaviorBurningProduces));
            api.RegisterBlockEntityBehaviorClass("PlanterAbsorbs", typeof(BlockEntityBehaviorPlanterAbsorbs));
            api.RegisterBlockEntityBehaviorClass("ProduceGas", typeof(BlockEntityBehaviorProduceGas));
            api.RegisterBlockEntityBehaviorClass("GasVent", typeof(BlockEntityBehaviorGasVent));

            GasSpreadBlockRadius = getBlockInRadius(GasConfig.Loaded.DefaultSpreadRadius);
            entityUtil = api.ModLoader.GetModSystem<EntityPartitioning>();

            harmony = new Harmony("com.grimm.asphyxiarebreathed");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            RealSmokeCompat.TryPatch(harmony, api);
        }

        public override void AssetsLoaded(ICoreAPI api)
        {
            base.AssetsLoaded(api);

            IAsset asset = api.Assets.Get("asphyxiarebreathed:config/gases.json");
            GasDictionary = asset.ToObject<Dictionary<string, GasInfo>>();
            if (GasDictionary == null) GasDictionary = new Dictionary<string, GasInfo>();
        }

        public override void Dispose()
        {
            harmony.UnpatchAll(harmony.Id);
            base.Dispose();
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);

            clientChannel = api.Network
                .RegisterChannel("gases")
                .RegisterMessageType(typeof(ChunkGasData))
                .SetMessageHandler<ChunkGasData>(onChunkData)
            ;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

            this.sapi = api;

            api.Event.ServerRunPhase(EnumServerRunPhase.ModsAndConfigReady, addGasBehavior);
            api.Event.SaveGameLoaded += onSaveGameLoaded;
            api.Event.GameWorldSave += onGameGettingSaved;
            api.Event.RegisterEventBusListener(OnSpreadGasBus, 10000, "spreadGas");

            serverChannel = api.Network
                .RegisterChannel("gases")
                .RegisterMessageType(typeof(ChunkGasData))
            ;

            api.ChatCommands.Create("gassys")
                .WithDescription("Manipulates the gas system")
                .RequiresPlayer()
                .RequiresPrivilege(Privilege.time)
                .BeginSubCommand("queue")
                    .HandleWith(args => RunGasCommand(args, "queue"))
                .EndSubCommand()
                .BeginSubCommand("reset")
                    .HandleWith(args => RunGasCommand(args, "reset"))
                .EndSubCommand()
                .BeginSubCommand("find")
                    .HandleWith(args => RunGasCommand(args, "find"))
                .EndSubCommand()
                .BeginSubCommand("stop")
                    .HandleWith(args => RunGasCommand(args, "stop"))
                .EndSubCommand()
                .BeginSubCommand("start")
                    .HandleWith(args => RunGasCommand(args, "start"))
                .EndSubCommand()
                .BeginSubCommand("cleanstart")
                    .HandleWith(args => RunGasCommand(args, "cleanstart"))
                .EndSubCommand()
                .BeginSubCommand("toggle")
                    .HandleWith(args => RunGasCommand(args, "toggle"))
                .EndSubCommand()
                .BeginSubCommand("pollution")
                    .HandleWith(args => RunGasCommand(args, "pollution"))
                .EndSubCommand();

            api.World.RegisterGameTickListener((dt) => {

                if (gasSpreader?.Stopping == true)
                {
                    lock (spreadGasLock)
                    {
                        Dictionary<BlockPos, Dictionary<string, float>> backup = new Dictionary<BlockPos, Dictionary<string, float>>();

                        foreach (var pos in spreadGasQueue)
                        {
                            if (!backup.ContainsKey(pos.Key)) backup.Add(pos.Key, pos.Value);
                        }

                        spreadGasQueue = backup;
                    }
                    gasSpreader.Stopping = false;
                    gasSpreader.Start(spreadGasQueue);
                }
            }, 30);

            api.World.RegisterGameTickListener(ProcessGasSeeps, 5000);
        }

        private TextCommandResult RunGasCommand(TextCommandCallingArgs args, string order)
        {
            IServerPlayer player = args.Caller.Player as IServerPlayer;
            if (player == null) return TextCommandResult.Error("This command requires a server player.", "");

            switch (order)
            {
                case "queue":
                    player.SendMessage(GlobalConstants.GeneralChatGroup, "Current Queue Count: " + spreadGasQueue.Count, EnumChatType.CommandSuccess);
                    break;
                case "reset":
                    lock (spreadGasLock)
                    {
                        Dictionary<BlockPos, Dictionary<string, float>> backup = new Dictionary<BlockPos, Dictionary<string, float>>();

                        foreach (var pos in spreadGasQueue)
                        {
                            if (!backup.ContainsKey(pos.Key)) backup.Add(pos.Key, pos.Value);
                        }

                        spreadGasQueue = backup;
                    }
                    break;
                case "find":
                    lock (spreadGasLock)
                    {
                        int count = 1;
                        foreach (var pos in spreadGasQueue)
                        {
                            player.SendMessage(GlobalConstants.GeneralChatGroup, String.Format("Position {0} in queue: X: {1}, Y: {2}, Z: {3}", count, pos.Key.X, pos.Key.Y, pos.Key.Z), EnumChatType.CommandSuccess);
                            count++;
                        }
                    }
                    break;
                case "stop":
                    gasSpreader.Stopping = true;
                    break;
                case "start":
                    gasSpreader.Stopping = false;
                    gasSpreader.Start(spreadGasQueue);
                    break;
                case "cleanstart":
                    lock (spreadGasLock)
                    {
                        Dictionary<BlockPos, Dictionary<string, float>> backup = new Dictionary<BlockPos, Dictionary<string, float>>();

                        foreach (var pos in spreadGasQueue)
                        {
                            if (!backup.ContainsKey(pos.Key)) backup.Add(pos.Key, pos.Value);
                        }

                        spreadGasQueue = backup;
                    }
                    gasSpreader.Stopping = false;
                    gasSpreader.Start(spreadGasQueue);
                    break;
                case "toggle":
                    gasSpreader.Paused = !gasSpreader.Paused;
                    break;
                case "pollution":
                    BlockPos playerPos = player.Entity.Pos.AsBlockPos;
                    int chunksize = GlobalConstants.ChunkSize;
                    Vec2i cpos = new Vec2i(playerPos.X / chunksize, playerPos.Z / chunksize);
                    StringBuilder info = new StringBuilder();

                    info.AppendLine(String.Format("Pollution in Chunk Column at positon X: {0}, Z: {1}", cpos.X, cpos.Y));
                    if (PollutionPerChunk != null && PollutionPerChunk.ContainsKey(cpos))
                        foreach (var gas in PollutionPerChunk[cpos]) info.AppendLine(Lang.Get("asphyxiarebreathed:gas-" + gas.Key) + ": " + gas.Value.ToString("#.#"));

                    player.SendMessage(GlobalConstants.GeneralChatGroup, info.ToString(), EnumChatType.CommandSuccess);
                    break;
            }

            return TextCommandResult.Success("", null);
        }

        private void OnSpreadGasBus(string eventName, ref EnumHandling handling, IAttribute data)
        {
            if (eventName != "spreadGas" || data == null) return;
            
            BlockPos spreadPos;

            Dictionary<string, float> gases =  GasHelper.DeserializeGasTreeData(data, out spreadPos);

            if (spreadPos == null) return;

            QueueGasExchange(gases, spreadPos);
        }

        private void onSaveGameLoaded()
        {
            spreadGasQueue = deserializeQueue("spreadGasQueue");
            PollutionPerChunk = deserializePollution("pollutionChunks");
            GasSeeps = deserializeGasSeeps("gasSeeps");
            gasSpreader = new GasSpreadingThread(sapi, this);
            gasSpreader.Start(spreadGasQueue);
        }

        private void onGameGettingSaved()
        {
            lock (spreadGasLock)
            {
                sapi.WorldManager.SaveGame.StoreData("spreadGasQueue", SerializerUtil.Serialize(spreadGasQueue));
                sapi.WorldManager.SaveGame.StoreData("pollutionChunks", SerializerUtil.Serialize(PollutionPerChunk));
                sapi.WorldManager.SaveGame.StoreData("gasSeeps", SerializerUtil.Serialize(GasSeeps));
            }
        }

        private Dictionary<BlockPos, Dictionary<string, float>> deserializeQueue(string name)
        {
            try
            {
                byte[] data = sapi.WorldManager.SaveGame.GetData(name);
                if (data != null)
                {
                    return SerializerUtil.Deserialize<Dictionary<BlockPos, Dictionary<string, float>>>(data);
                }
            }
            catch (Exception e)
            {
                sapi.World.Logger.Error("Failed loading Gas Spread Queue.{0}. Resetting. Exception: {1}", name, e);
            }
            return new Dictionary<BlockPos, Dictionary<string, float>>();
        }

        private Dictionary<Vec2i, Dictionary<string, double>> deserializePollution(string name)
        {
            try
            {
                byte[] data = sapi.WorldManager.SaveGame.GetData(name);
                if (data != null)
                {
                    return SerializerUtil.Deserialize<Dictionary<Vec2i, Dictionary<string, double>>>(data);
                }
            }
            catch (Exception e)
            {
                sapi.World.Logger.Error("Failed loading Pollution.{0}. Resetting. Exception: {1}", name, e);
            }
            return new Dictionary<Vec2i, Dictionary<string, double>>();
        }

        private Dictionary<BlockPos, GasSeepSource> deserializeGasSeeps(string name)
        {
            try
            {
                byte[] data = sapi.WorldManager.SaveGame.GetData(name);
                if (data != null)
                {
                    return SerializerUtil.Deserialize<Dictionary<BlockPos, GasSeepSource>>(data);
                }
            }
            catch (Exception e)
            {
                sapi.World.Logger.Error("Failed loading Gas Seeps.{0}. Resetting. Exception: {1}", name, e);
            }
            return new Dictionary<BlockPos, GasSeepSource>();
        }

        private void onChunkData(ChunkGasData msg)
        {
            IWorldChunk chunk = api.World.BlockAccessor.GetChunk(msg.chunkX, msg.chunkY, msg.chunkZ);
            if (chunk != null)
            {
                chunk.SetModdata("gases", msg.Data);
            }
        }

        void saveGases(Dictionary<int, Dictionary<string, float>> gases, BlockPos pos)
        {
            int chunksize = GlobalConstants.ChunkSize;
            int chunkX = pos.X / chunksize;
            int chunkY = pos.Y / chunksize;
            int chunkZ = pos.Z / chunksize;

            byte[] data = SerializerUtil.Serialize(gases);

            IWorldChunk chunk = api.World.BlockAccessor.GetChunk(chunkX, chunkY, chunkZ);
            chunk.SetModdata("gases", data);

            // Todo: Send only to players that have this chunk in their loaded range
            serverChannel?.BroadcastPacket(new ChunkGasData() { chunkX = chunkX, chunkY = chunkY, chunkZ = chunkZ, Data = data });
        }

        Dictionary<int, Dictionary<string, float>> getOrCreateGasesAt(BlockPos pos)
        {
            byte[] data;

            IWorldChunk chunk = api.World.BlockAccessor.GetChunkAtBlockPos(pos);
            if (chunk == null) return null;

            data = chunk.GetModdata("gases");

            Dictionary<int, Dictionary<string, float>> gasesOfChunk = null;

            if (data != null)
            {
                try
                {
                    gasesOfChunk = SerializerUtil.Deserialize<Dictionary<int, Dictionary<string, float>>>(data);
                }
                catch (Exception)
                {
                    gasesOfChunk = new Dictionary<int, Dictionary<string, float>>();
                }
            }
            else
            {
                gasesOfChunk = new Dictionary<int, Dictionary<string, float>>();
            }

            return gasesOfChunk;
        }

        static Dictionary<int, Dictionary<string, float>> getOrCreateGasesAt(IWorldChunk chunk)
        {
            byte[] data;

            data = chunk.GetModdata("gases");

            Dictionary<int, Dictionary<string, float>> gasesOfChunk = null;

            if (data != null)
            {
                try
                {
                    gasesOfChunk = SerializerUtil.Deserialize<Dictionary<int, Dictionary<string, float>>>(data);
                }
                catch (Exception)
                {
                    gasesOfChunk = new Dictionary<int, Dictionary<string, float>>();
                }
            }
            else
            {
                gasesOfChunk = new Dictionary<int, Dictionary<string, float>>();
            }

            return gasesOfChunk;
        }

        private void addGasBehavior()
        {
            if (!GasConfig.Loaded.GasesEnabled) return;
            foreach (Block block in api.World.Blocks)
            {
                if (block.BlockId != 0)
                {
                    block.BlockBehaviors = block.BlockBehaviors.Append(new BlockBehaviorGas(block));
                    block.CollectibleBehaviors = block.CollectibleBehaviors.Append(new BlockBehaviorGas(block));
                }
            }

        }

        public Dictionary<string, float> GetGases(BlockPos pos)
        {
            Dictionary<int, Dictionary<string, float>> gasesOfChunk = getOrCreateGasesAt(pos);
            Dictionary<string, float> realSmokeGases = RealSmokeCompat.GetSmokeGasesAt(api.World, pos);
            if (gasesOfChunk == null) return realSmokeGases;

            int index3d = toLocalIndex(pos);
            if (!gasesOfChunk.ContainsKey(index3d)) return realSmokeGases;

            return RealSmokeCompat.MergeGasReports(gasesOfChunk[index3d], realSmokeGases);
        }

        public float GetGas(BlockPos pos, string name)
        {
            Dictionary<string, float> gasesHere = GetGases(pos);

            if (gasesHere == null || !gasesHere.ContainsKey(name)) return 0;

            return gasesHere[name];
        }

        public Dictionary<string, float> RemoveGases(BlockPos pos)
        {
            Dictionary<int, Dictionary<string, float>> gasesOfChunk = getOrCreateGasesAt(pos);
            if (gasesOfChunk == null) return null;

            int index3d = toLocalIndex(pos);
            if (!gasesOfChunk.ContainsKey(index3d) || gasesOfChunk[index3d] == null) return null;

            Dictionary<string, float> result = new Dictionary<string, float>(gasesOfChunk[index3d]);

            if (gasesOfChunk.Remove(index3d))
            {
                saveGases(gasesOfChunk, pos);
                return result;
            }

            return null;
        }

        public void SetGases(BlockPos pos, Dictionary<string, float> gasputhere)
        {
            Dictionary<int, Dictionary<string, float>> gasesOfChunk = getOrCreateGasesAt(pos);
            if (gasesOfChunk == null) return;

            int index3d = toLocalIndex(pos);
            if (!gasesOfChunk.ContainsKey(index3d))
            {
                gasesOfChunk.Add(index3d, gasputhere);
            }
            else
            {
                gasesOfChunk[index3d] = gasputhere;
            }



            saveGases(gasesOfChunk, pos);
        }

        public float GetAirAmount(BlockPos pos)
        {
            Dictionary<string, float> gasesHere = GetGases(pos);

            if (gasesHere == null) return 1;

            float conc = 0;

            foreach (var gas in gasesHere)
            {
                if (GasDictionary.ContainsKey(gas.Key))
                {
                    if (GasDictionary[gas.Key] != null) conc += gas.Value * GasDictionary[gas.Key].QualityMult; else conc += gas.Value;
                }
            }

            if (conc >= 2) return -1;
            if (conc < 0) return 1;
            return 1- conc;
        }

        public float GetAcidity(BlockPos pos)
        {
            Dictionary<string, float> gasesHere = GetGases(pos);

            if (gasesHere == null) return 0;

            float conc = 0;

            foreach (var gas in gasesHere)
            {
                if (GasDictionary.ContainsKey(gas.Key))
                {
                    if (GasDictionary[gas.Key] != null && GasDictionary[gas.Key].Acidic) conc += gas.Value;
                    if (conc >= 1) return 1;
                }
            }

            return conc;
        }

        public bool IsVolatile(BlockPos pos)
        {
            Dictionary<string, float> gasesHere = GetGases(pos);

            if (gasesHere == null) return false;

            foreach (var gas in gasesHere)
            {
                if (GasDictionary.ContainsKey(gas.Key))
                {
                    if (GasDictionary[gas.Key].FlammableAmount > 0 && gas.Value >= GasDictionary[gas.Key].FlammableAmount) return true;
                }
            }

            return false;
        }

        public bool ShouldExplode(BlockPos pos)
        {
            Dictionary<string, float> gasesHere = GetGases(pos);

            if (gasesHere == null) return false;

            foreach (var gas in gasesHere)
            {
                if (GasDictionary.ContainsKey(gas.Key))
                {
                    if (GasDictionary[gas.Key].ExplosionAmount <= gas.Value) return true;
                }
            }

            return false;
        }

        public bool IsToxic(string name, float amount)
        {
            if (!GasDictionary.ContainsKey(name)) return true;

            return amount > GasDictionary[name].ToxicAt;
        }

        public void ReleaseGasSeep(BlockPos pos)
        {
            if (pos == null || !GasSeeps.TryGetValue(pos, out GasSeepSource seep)) return;

            QueueGasExchange(ScaleGasDict(seep.Gases, GasConfig.Loaded.OreSeepBreakMultiplier), pos);
            GasSeeps.Remove(pos);
        }

        public void TryCreateGasSeep(IWorldAccessor world, Block sourceBlock, BlockPos pos, Dictionary<string, float> gases)
        {
            if (!GasConfig.Loaded.OreSeepsEnabled || world?.Side != EnumAppSide.Server || pos == null || gases == null || gases.Count < 1) return;
            if (world.Rand.NextDouble() > GasConfig.Loaded.OreSeepChance) return;

            string sourceType = GetSeepBlockType(sourceBlock);
            if (sourceType == null) return;

            Dictionary<string, float> seepGases = GetSeepGases(gases);
            if (seepGases.Count < 1) return;

            List<BlockPos> candidates = new List<BlockPos>();

            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                BlockPos checkPos = pos.AddCopy(face);
                if (GasSeeps.ContainsKey(checkPos)) continue;

                Block checkBlock = world.BlockAccessor.GetBlock(checkPos);
                if (GetSeepBlockType(checkBlock) == sourceType) candidates.Add(checkPos);
            }

            if (candidates.Count < 1) return;

            BlockPos seepPos = candidates[world.Rand.Next(candidates.Count)].Copy();
            GasSeeps[seepPos] = new GasSeepSource()
            {
                BlockType = sourceType,
                Gases = seepGases,
                LastProduced = world.Calendar.TotalHours
            };
        }

        private void ProcessGasSeeps(float dt)
        {
            if (!GasConfig.Loaded.OreSeepsEnabled || GasSeeps.Count < 1) return;

            double totalHours = sapi.World.Calendar.TotalHours;

            foreach (var entry in GasSeeps.ToArray())
            {
                Block block = sapi.World.BlockAccessor.GetBlock(entry.Key);
                if (GetSeepBlockType(block) != entry.Value.BlockType)
                {
                    GasSeeps.Remove(entry.Key);
                    continue;
                }

                if (totalHours - entry.Value.LastProduced < GasConfig.Loaded.OreSeepUpdateHours) continue;

                entry.Value.LastProduced = totalHours;
                QueueGasExchange(new Dictionary<string, float>(entry.Value.Gases), entry.Key);
            }
        }

        private Dictionary<string, float> GetSeepGases(Dictionary<string, float> gases)
        {
            Dictionary<string, float> seepGases = new Dictionary<string, float>();

            foreach (var gas in gases)
            {
                if (gas.Key == "coaldust" || gas.Key == "silicadust") continue;
                if (gas.Key == "RADIUS" || gas.Key.StartsWith("THISISA") || gas.Key.StartsWith("IGNORE")) continue;
                if (gas.Value <= 0) continue;

                seepGases[gas.Key] = gas.Value * GasConfig.Loaded.OreSeepAmountMultiplier;
            }

            return seepGases;
        }

        private Dictionary<string, float> ScaleGasDict(Dictionary<string, float> gases, float multiplier)
        {
            Dictionary<string, float> result = new Dictionary<string, float>();
            if (gases == null) return result;

            foreach (var gas in gases)
            {
                result[gas.Key] = gas.Value * multiplier;
            }

            return result;
        }

        private string GetSeepBlockType(Block block)
        {
            string path = block?.Code?.Path;
            if (path == null) return null;

            string[] parts = path.Split('-');
            if (parts.Length >= 3 && parts[0] == "ore")
            {
                string gradeOrType = parts[1];
                if (gradeOrType == "poor" || gradeOrType == "medium" || gradeOrType == "rich" || gradeOrType == "bountiful") return "ore-" + parts[2];

                return "ore-" + gradeOrType;
            }

            return block.Code.ToString();
        }

        public void SetupExplosion(BlockPos pos, int radius)
        {
            if (pos == null || radius < 0) return;

            if (!ExplosionQueue.ContainsKey(pos))
            {
                Dictionary<string, float> dict = new Dictionary<string, float>();
                dict.Add("THISISANEXPLOSION", -100);
                int blocks = getBlockInRadius(radius);
                dict.Add("nitrogendioxide", 0.3f * blocks);
                dict.Add("carbonmonoxide", 0.01f * blocks);
                ExplosionQueue[pos] = dict;
            }
            else if (ExplosionQueue[pos].ContainsKey("RADIUS") && ExplosionQueue[pos]["RADIUS"] < radius)
            {
                ExplosionQueue[pos]["RADIUS"] = radius;
            }
        }

        public void EnqueueExplosion(BlockPos pos)
        {
            if (pos == null) return;

            if (!ExplosionQueue.ContainsKey(pos)) return;
            
            QueueGasExchange(ExplosionQueue[pos], pos);
            ExplosionQueue.Remove(pos);
        }

        public void AddToExplosion(BlockPos pos, Dictionary<string, float> gases)
        {
            if (pos == null || !ExplosionQueue.ContainsKey(pos)) return;

            Dictionary<string, float> dest = ExplosionQueue[pos];

            GasHelper.MergeGasDicts(gases, ref dest);

            ExplosionQueue[pos] = dest;
        }

        public void AddPollution(BlockPos pos, string gas, float value)
        {
            if (pos == null || gas == null || value == 0) return;

            int chunksize = GlobalConstants.ChunkSize;
            Vec2i columm = new Vec2i(pos.X / chunksize, pos.Z / chunksize);

            if (!PollutionPerChunk.ContainsKey(columm))
            {
                PollutionPerChunk.Add(columm, new Dictionary<string, double>());
                PollutionPerChunk[columm].Add(gas, value);
            }
            else
            {
                if (!PollutionPerChunk[columm].ContainsKey(gas))
                {
                    PollutionPerChunk[columm].Add(gas, value);
                }
                else
                {
                    PollutionPerChunk[columm][gas] += value;
                }
            }

            if (PollutionPerChunk[columm][gas] < 0) PollutionPerChunk[columm][gas] = 0;
        }

        public void QueueGasExchange(Dictionary<string, float> adds, BlockPos pos, float scrub = 0, bool ignoreLiquids = false, bool ignoreSide = false)
        {
            if (adds == null) adds = new Dictionary<string, float>();

            if (scrub > 0) adds.Add("THISISAPLANT", 0);
            if (ignoreLiquids) adds.Add("IGNORELIQUIDS", 0);
            if (ignoreSide) adds.Add("IGNORESOLIDCHECK", 0);

            BlockPos temp = pos.Copy();

            lock (spreadGasLock)
            {
                if (!spreadGasQueue.ContainsKey(temp)) spreadGasQueue.Add(temp, adds);
                else
                {
                    foreach (var gas in adds)
                    {
                        if (!spreadGasQueue[temp].ContainsKey(gas.Key)) spreadGasQueue[temp].Add(gas.Key, gas.Value);
                        else spreadGasQueue[temp][gas.Key] += gas.Value;
                    }
                }
            }
        }

        static int toLocalIndex(BlockPos pos)
        {
            return MapUtil.Index3d(pos.X % 32, pos.Y % 32, pos.Z % 32, 32, 32);
        }

        bool findPosInLayers(BlockPos pos, HashSet<BlockPos>[] layers)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].Contains(pos)) return true;
            }

            return false;
        }

        int getBlockInRadius(int radius)
        {
            Vec4i[] comp = new Vec4i[] { new Vec4i(1, 0, 0, 1), new Vec4i(-1, 0, 0, 1), new Vec4i(0, -1, 0, 1), new Vec4i(0, 1, 0, 1), new Vec4i(0, 0, 1, 1), new Vec4i(0, 0, -1, 1) };

            List<Vec4i> counter = new List<Vec4i>();
            Queue<Vec4i> next = new Queue<Vec4i>();
            Vec4i origin = new Vec4i(0, 0, 0, 0);
            counter.Add(origin);
            next.Enqueue(origin);

            while (next.Count > 0)
            {
                Vec4i current = next.Dequeue();

                foreach (Vec4i side in comp)
                {
                    Vec4i test = new Vec4i(side.X + current.X, side.Y + current.Y, side.Z + current.Z, side.W + current.W);
                    if (test.W <= radius && !counter.Contains(test))
                    {
                        next.Enqueue(test);
                        counter.Add(test);
                    }
                }
            }

            return counter.Count;
        }

        class GasSpreadingThread
        {
            int gasSpreadTick = 10;
            IBlockAccessor blockAccessor;
            ICoreServerAPI sapi;
            Dictionary<BlockPos, Dictionary<string, float>> checkSpread;
            GasSystem gasSys;

            public bool Stopping { get; set; }
            public bool Paused { get; set; }

            public GasSpreadingThread(ICoreServerAPI sapi, GasSystem gassys)
            {
                this.sapi = sapi;
                this.gasSys = gassys;
            }

            public void Start(Dictionary<BlockPos, Dictionary<string, float>> checkSpread)
            {
                this.checkSpread = checkSpread;

                Thread thread = new Thread(() =>
                {
                    while (!sapi.Server.IsShuttingDown && !Stopping)
                    {
                        if (!Paused && GasConfig.Loaded.GasesEnabled)
                        {
                            blockAccessor = sapi.World.BlockAccessor;
                            for (int i = 0; i < 100; i++)
                            {
                                if (checkSpread.Count <= 0) break;

                                BlockPos current = null;
                                Dictionary<string, float> gases = null;

                                lock (spreadGasLock)
                                {
                                    try
                                    {
                                        current = checkSpread.Keys.First();
                                        gases = checkSpread[current];
                                        checkSpread.Remove(current);
                                    }
                                    catch (Exception)
                                    {
                                        Stopping = true;
                                    }
                                }

                                if (Stopping) break;
                                AddAndDistributeGas(gases, current);
                            }

                            Thread.Sleep(gasSpreadTick);
                        }
                    }
                });

                thread.IsBackground = true;
                thread.Name = "CheckGasSpread";
                thread.Start();
            }

            public void AddAndDistributeGas(Dictionary<string, float> adds, BlockPos pos)
            {
                if (pos.Y < 1 || pos.Y > blockAccessor.MapSizeY) return;

                Dictionary<string, float> collectedGases = adds ?? new Dictionary<string, float>();
                bool combusted = collectedGases.ContainsKey("THISISANEXPLOSION"), ignoreLiquid = collectedGases.ContainsKey("IGNORELIQUIDS"), ignoreCheck = collectedGases.ContainsKey("IGNORESOLIDCHECK");
                float plantNear = collectedGases.ContainsKey("THISISAPLANT") ? collectedGases["THISISAPLANT"] : 0;
                collectedGases.Remove("THISISANEXPLOSION");
                collectedGases.Remove("THISISAPLANT");
                collectedGases.Remove("IGNORELIQUIDS");
                collectedGases.Remove("IGNORESOLIDCHECK");
                int radius = GasConfig.Loaded.DefaultSpreadRadius;
                if (collectedGases.ContainsKey("RADIUS")) { radius = (int)collectedGases["RADIUS"]; collectedGases.Remove("RADIUS"); }
                if (radius < 1) radius = 0;
                Queue<Vec3i> checkQueue = new Queue<Vec3i>();
                List<GasChunk> chunks = new List<GasChunk>();
                Cuboidi bounds = new Cuboidi(pos.X - radius, pos.Y - radius, pos.Z - radius, pos.X + radius, pos.Y + radius, pos.Z + radius);
                HashSet<BlockPos>[] layers = new HashSet<BlockPos>[bounds.MaxY - bounds.MinY + 1];
                Dictionary<int, Block> blocks = new Dictionary<int, Block>();
                float windspeed = -1;
                int chunksize = GlobalConstants.ChunkSize;
                int totalBlockCount = 1;
                bool openAir = false;

                for (int i = 0; i < layers.Length; i++)
                {
                    layers[i] = new HashSet<BlockPos>();
                }

                for (int x = bounds.MinX / chunksize; x <= bounds.MaxX / chunksize; x++)
                {
                    for (int y = bounds.MinY / chunksize; y <= bounds.MaxY / chunksize; y++)
                    {
                        for (int z = bounds.MinZ / chunksize; z <= bounds.MaxZ / chunksize; z++)
                        {
                            IWorldChunk chunk = blockAccessor.GetChunk(x, y, z);

                            if (chunk != null)
                            {
                                chunks.Add(new GasChunk(chunk, getOrCreateGasesAt(chunk), x, y, z));
                            }
                        }
                    }
                }
                if (chunks.Count < 1) return;

                checkQueue.Enqueue(pos.ToVec3i());
                layers[pos.Y - bounds.MinY].Add(pos);
                Block starter = blockAccessor.GetBlock(pos);
                blocks.Add(starter.BlockId, starter);

                GasChunk originChunk = null;

                foreach (GasChunk chunk in chunks)
                {
                    if (chunk.Compare(pos, chunksize))
                    {
                        originChunk = chunk;
                        break;
                    }
                }

                if (originChunk == null) return;

                originChunk.TakeGas(ref collectedGases, toLocalIndex(pos));

                BlockFacing[] faces = BlockFacing.ALLFACES;
                BlockPos curPos = new BlockPos(pos.dimension);

                while (checkQueue.Count > 0)
                {
                    //Gets Parent info
                    Vec3i bpos = checkQueue.Dequeue();

                    Block parent = null;
                    GasChunk parentChunk = null;

                    foreach (GasChunk chunk in chunks)
                    {
                        if (chunk.Compare(bpos.AsBlockPos, chunksize))
                        {
                            parentChunk = chunk;
                            break;
                        }
                    }

                    if (parentChunk == null) continue;

                    int parentBlockId = parentChunk.Chunk.UnpackAndReadBlock(toLocalIndex(bpos.AsBlockPos), BlockLayersAccess.Default);
                    if (!TryResolveBlock(parentBlockId, blocks, out parent)) continue;

                    //Process Children
                    foreach (BlockFacing facing in faces)
                    {
                        //Checks to see if this is a valid pos
                        if (!ignoreCheck && SolidCheck(parent, facing)) continue;
                        curPos.Set(bpos.X + facing.Normali.X, bpos.Y + facing.Normali.Y, bpos.Z + facing.Normali.Z);
                        if (!bounds.Contains(curPos) || layers[curPos.Y - bounds.MinY].Contains(curPos)) continue;
                        if (curPos.Y < 0 || curPos.Y > blockAccessor.MapSizeY) continue;

                        GasChunk localArea = null;
                        int chunkBid = toLocalIndex(curPos);
                        Block atPos = null;

                        foreach (GasChunk chunk in chunks)
                        {
                            if (chunk.Compare(curPos, chunksize))
                            {
                                localArea = chunk;
                                break;
                            }
                        }

                        if (localArea == null) continue;

                        int blockId = localArea.Chunk.UnpackAndReadBlock(toLocalIndex(curPos), BlockLayersAccess.Default);

                        if (!TryResolveBlock(blockId, blocks, out atPos)) continue;

                        if (!ignoreCheck && SolidCheck(atPos, facing.Opposite)) continue;
                        bool mediumComp = ignoreLiquid || !atPos.IsLiquid() || (parent.IsLiquid() && atPos.IsLiquid());
                        if (!mediumComp) continue;

                        //Confirmed this is a valid pos, now check other things
                        localArea.TakeGas(ref collectedGases, chunkBid);

                        if (blockAccessor.GetRainMapHeightAt(curPos) < curPos.Y)
                        {
                            openAir = true;
                            windspeed = GetWindspeed(blockAccessor.GetWindSpeedAt(curPos.ToVec3d()), windspeed);
                        }

                        if (IsPlant(atPos)) plantNear ++;
                        layers[curPos.Y - bounds.MinY].Add(curPos.Copy());
                        checkQueue.Enqueue(curPos.ToVec3i());
                        totalBlockCount++;
                    }
                }

                //Finished getting positions, now deal with gases
                Dictionary<string, float> modifier = new Dictionary<string, float>(collectedGases);

                //Convert gases to their burned state, if this is an explosion
                if (combusted)
                {
                    foreach (var gas in collectedGases)
                    {
                        if (GasDictionary.ContainsKey(gas.Key) && (GasDictionary[gas.Key].FlammableAmount <= 1 || GasDictionary[gas.Key].ExplosionAmount <= 1))
                        {
                            if (GasDictionary[gas.Key].BurnInto != null) GasHelper.MergeGasIntoDict(GasDictionary[gas.Key].BurnInto, gas.Value, ref modifier);

                            modifier.Remove(gas.Key);
                        }
                    }

                    collectedGases = new Dictionary<string, float>(modifier);
                }
                
                //Spread gases
                foreach (var gas in collectedGases)
                {
                    bool light = false;
                    bool plant = false;
                    bool distribute = false;
                    float wind = 0;
                    bool acid = false;
                    bool pollutant = false;

                    if (GasDictionary.ContainsKey(gas.Key) && GasDictionary[gas.Key] != null)
                    {
                        light = GasDictionary[gas.Key].Light;
                        plant = GasDictionary[gas.Key].PlantAbsorb;
                        distribute = GasDictionary[gas.Key].Distribute;
                        wind = GasDictionary[gas.Key].VentilateSpeed;
                        acid = GasDictionary[gas.Key].Acidic;
                        pollutant = GasDictionary[gas.Key].Pollutant;
                    }

                    if (plant && plantNear > 0)
                    {
                        modifier[gas.Key] -= plantNear;
                    }

                    if (openAir && windspeed >= wind)
                    {
                        gasSys.AddPollution(pos, gas.Key, gas.Value);

                        continue;
                    }

                    if (modifier[gas.Key] <= 0) continue;

                    if (distribute)
                    {
                        float giveaway = Math.Min(gas.Value / totalBlockCount, 1);
                        for (int i = layers.Length - 1; i > 0; i--)
                        {
                            GasChunk localArea = null;

                            foreach (BlockPos pil in layers[i])
                            {
                                if (localArea == null || !localArea.Compare(pil, chunksize))
                                {
                                    foreach (GasChunk chunk in chunks)
                                    {
                                        if (chunk.Compare(pil, chunksize))
                                        {
                                            localArea = chunk;
                                            break;
                                        }
                                    }
                                }

                                if (localArea == null) continue;

                                localArea.SetGas(gas.Key, giveaway, toLocalIndex(pil));
                            }
                        }
                    }
                    else if (light) //Distribute light gases
                    {
                        for (int i = layers.Length - 1; i > 0; i--)
                        {
                            if (layers[i].Count < 1) continue;
                            float giveaway = 1;
                            if (modifier[gas.Key] < layers[i].Count) giveaway = modifier[gas.Key] / layers[i].Count; else giveaway = 1;

                            GasChunk localArea = null;

                            foreach (BlockPos pil in layers[i])
                            {
                                if (localArea == null || !localArea.Compare(pil, chunksize))
                                {
                                    foreach (GasChunk chunk in chunks)
                                    {
                                        if (chunk.Compare(pil, chunksize))
                                        {
                                            localArea = chunk;
                                            break;
                                        }
                                    }
                                }

                                if (localArea == null) continue;

                                localArea.SetGas(gas.Key, giveaway, toLocalIndex(pil));
                            }

                            modifier[gas.Key] -= layers[i].Count;
                            if (modifier[gas.Key] <= 0) break;
                        }
                    }
                    else //Distribute heavy gases
                    {

                        for (int i = 0; i < layers.Length; i++)
                        {
                            if (layers[i].Count < 1) continue;
                            float giveaway = 1;
                            if (modifier[gas.Key] < layers[i].Count) giveaway = modifier[gas.Key] / layers[i].Count; else giveaway = 1;

                            GasChunk localArea = null;

                            foreach (BlockPos pil in layers[i])
                            {
                                if (localArea == null || !localArea.Compare(pil, chunksize))
                                {
                                    foreach (GasChunk chunk in chunks)
                                    {
                                        if (chunk.Compare(pil, chunksize))
                                        {
                                            localArea = chunk;
                                            break;
                                        }
                                    }
                                }

                                if (localArea == null) continue;

                                localArea.SetGas(gas.Key, giveaway, toLocalIndex(pil));
                            }

                            modifier[gas.Key] -= layers[i].Count;
                            if (modifier[gas.Key] <= 0) break;
                        }
                    }
                }

                //Save Time!!!
                foreach (GasChunk chunk in chunks)
                {
                    chunk.SaveChunk(serverChannel);
                }
            }

            public bool SolidCheck(Block block, BlockFacing face)
            {
                if (block.Attributes?.KeyExists("gassysSolidSides") == true)
                {
                    return block.Attributes["gassysSolidSides"].IsTrue(face.Code);
                }

                return block.SideSolid[face.Index];
            }

            private bool TryResolveBlock(int blockId, Dictionary<int, Block> blocks, out Block block)
            {
                if (blocks.TryGetValue(blockId, out block)) return block != null;

                block = blockAccessor.GetBlock(blockId);
                if (block == null) return false;

                blocks[blockId] = block;
                return true;
            }

            public bool IsPlant(Block block)
            {
                if (block.Attributes?.KeyExists("gassysPlant") == true) return block.Attributes["gassysPlant"].AsBool();

                return block.BlockMaterial == EnumBlockMaterial.Plant || block.BlockMaterial == EnumBlockMaterial.Leaves;
            }

            public float GetWindspeed(Vec3d windVec, float current)
            {
                float newwind = current;
                float x = (float)Math.Abs(windVec.X);
                float y = (float)Math.Abs(windVec.Y);
                float z = (float)Math.Abs(windVec.Z);

                newwind = x > newwind ? x : newwind;
                newwind = y > newwind ? y : newwind;
                newwind = z > newwind ? z : newwind;

                return newwind;
            }
        }
    }
}
