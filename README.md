# Asphyxia: Rebreathed

Asphyxia: Rebreathed adds a full gas simulation system to Vintage Story using chunk-based data instead of physical gas blocks. Gases can build up, spread through connected spaces, vent outdoors, ignite, explode, poison creatures, affect breathing, and interact with the environment dynamically.

Requires Vintage Story 1.22.2.

## Features

- Uses chunk-stored gas data rather than placeholder gas blocks.
- Multiple gases can exist in the same space at varying concentrations.
- Gases spread naturally through caves, buildings, and open areas while respecting walls, doors, trapdoors, liquids, plants, wind, and open sky.
- Light gases rise, heavy gases sink, and neutral gases distribute more evenly.
- Outdoor ventilation can clear gases while tracking environmental pollution.
- Certain plants and planted containers can absorb compatible gases.
- Gas data syncs between server and client for gameplay effects and inspection.

## Included Gases

- Carbon Dioxide
- Carbon Monoxide
- Smoke
- Methane
- Hydrogen
- Hydrogen Sulfide
- Sulfur Dioxide
- Nitrogen Dioxide
- Coal Dust
- Silica Dust

## Gameplay Systems

### Breathing

Creatures that use vanilla breathing mechanics are integrated into the gas system. Poor air quality reduces available breath, eventually leading to suffocation if air runs out completely.

Toxic gases can apply a range of negative effects, including slower movement, reduced mining speed, weaker attacks, slower healing, reduced max health, armor damage, and faster oxygen loss.

Players and creatures can also exhale carbon dioxide when exhaling is enabled.

### Fire, Smoke, and Combustion

Burning blocks and block entities can generate gases such as carbon monoxide and carbon dioxide. Supported sources currently include fire, firepits, forges, bloomeries, coal piles, charcoal pits, pit kilns, torches, torch holders, oil lamps, and boilers.

Flammable gases can ignite, and explosive gases can detonate when concentrations become high enough.

### Mining and Caving

Mining and explosions can release dangerous gases and dust into surrounding areas.

Examples include:

- Silica dust from rock and stalagmites
- Methane and coal dust from coal ores
- Coal dust from placing or removing coal and charcoal piles
- Hydrogen sulfide from sulfide ores
- Sulfur dioxide from exploded sulfide ores and sulfur ore
- Nitrogen dioxide from saltpeter

Dust generation is reduced or prevented when the source material is wet unless otherwise configured.

### Explosions

Explosions generate nitrogen dioxide and carbon monoxide based on blast size. Blocks with explosion gas behaviors can contribute additional gases to the resulting cloud.

Explosions can also ignite and consume flammable gases, converting them into their configured byproducts.

### Acidic Liquids

Acidic gases can increase acidity around nearby liquids. High acidity can poison entities and damage armor unless the equipment is marked as corrosion resistant.

### Food Preservation

When enabled, containers stored in poor air quality can receive reduced perish rates.

### RealSmoke Compatibility

With compatibility enabled, nearby RealSmoke smoke entities are treated as carbon monoxide and carbon dioxide during gas checks. The mod can also disable RealSmoke’s own suffocation system so breathing effects are handled consistently through Asphyxia instead.

## Configuration

The mod generates `asphyxiarebreathed.json`. Legacy `GasConfig.json` files are still supported if present.

Some important settings include:

- `GasesEnabled`
- `BreathingEnabled`
- `PlayerBreathingEnabled`
- `Explosions`
- `FlammableGas`
- `PickaxeExplosionChance`
- `Smoke`
- `Acid`
- `Exhaling`
- `ContainerBonus`
- `AllowScuba`
- `AllowMasks`
- `ToxicEffects`
- `RealSmokeCompatibility`
- `DisableRealSmokeAsphyxiation`
- `OreSeepsEnabled`
- `DefaultSpreadRadius`

Ore seeps are fully implemented but disabled by default.

## Admin Commands

All commands begin with `/gassys`.

- `/gassys queue` displays queued gas spread events.
- `/gassys reset` rebuilds the spread queue.
- `/gassys find` lists queued gas event locations.
- `/gassys stop` stops the gas spread thread.
- `/gassys start` starts the gas spread thread.
- `/gassys cleanstart` starts the system with a rebuilt queue.
- `/gassys toggle` pauses or resumes gas spreading.
- `/gassys pollution` shows recorded pollution for the current chunk column.

## Modding Support

Gas definitions are stored in:

`assets/asphyxiarebreathed/config/gases.json`

### Supported block and block entity behaviors include:

- `MineGas`
- `PlaceGas`
- `ExplosionGas`
- `SparkGas`
- `BurningProduces`
  - `smolderGas` is a gas dictionary of what to spread while smoldering.
  - `smolderGasByType` maps block code wildcards to smolder gas dictionaries.
  - `smolderWhenLit` defaults to false. If true, the block uses smolder gas while burning instead of only when unlit.
  - `gassysSmolderWhenLit`: true //This attribute marks a lit item as a smolder source while carried.
  - `gassysSmolderGas`: {"smoke": 0.1} //This attribute is a gas dictionary released by smoldering carried items when backpack smolder emissions are enabled.
- `PlanterAbsorbs`
- `ProduceGas`
- "GasVent": Block entities with this behavior produce gas only when environmental conditions allow it
  - Works with any block entity
  - `produceGas` is a gas dictionary of what to spread.
  - `produceGasByType` maps block code wildcards to gas dictionaries, allowing one patch to define different gases for different block variants.
  - `updateMS` is an integer of how often in milliseconds the production function is checked.
  - `updateHours` is the in-game hourly interval between successful gas releases.
  - `minY` and `maxY` limit the block Y range where the vent can produce gas.
  - `maxLight` prevents venting when the block's max light level is above this value.
  - `requireSkyless` defaults to true. If true, the vent will not produce gas when open to the sky.
  - `ignoreLiquids` defaults to false. If true, the gas spread event ignores the normal liquid spreading restriction.
  - `ignoreSides` defaults to false. If true, the gas spread event ignores solid side checks.

Supported entity behaviors include:

- `air`
- `gasinteract`

Gas-related attributes include:

- `gassysSolidSides`
- `gassysPlant`
- `gassysAntiCorrosion`
- `gassysGasMaskProtection`
- `gassysScubaMask`
- `gassysScubaTank`
- `gassysSmolderGas`
- `gassysSmolderWhenLit`

Other mods can interact with the system through `GasHelper` to read gas data, evaluate air quality, check toxicity or acidity, gather gases from an area, or queue gas spread events through the event bus.

## Current Limitations

Pollution tracking is implemented, but larger climate systems such as greenhouse effects and acid rain are not yet included.

Gas spreading runs on a separate background thread, so changes may not appear instantly after a spread event is queued.

Ore seeps are implemented in code but disabled by default.
