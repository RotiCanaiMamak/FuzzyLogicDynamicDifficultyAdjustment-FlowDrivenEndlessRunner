# Fuzzy DDA Endless Runner

A 2D endless runner prototype built in Unity that uses a fuzzy logic Dynamic Difficulty Adjustment (DDA) system to adapt gameplay difficulty to the player’s performance.

The player moves continuously across procedurally generated terrain, jumps over obstacles, and performs backflips. The DDA system evaluates ground skill, aerial skill, and session duration to dynamically adjust movement speed, obstacle density, and slide-terrain frequency.

## Features

- Endless 2D runner gameplay
- Procedurally generated terrain chunks
- Flat terrain, slopes, small slides, medium slides, and big slides
- Random obstacle spawning on eligible terrain
- Player jumping, slope alignment, surface adhesion, and backflip mechanics
- Near miss and collision tracking
- Fuzzy logic Dynamic Difficulty Adjustment system
- Dynamic adjustment of:
  - Player movement speed
  - Obstacle spawn chance
  - Small, medium, and big slide frequency
- Ground skill calculation based on near misses and collisions
- Aerial skill calculation based on completed backflips
- Exponential moving average smoothing for player skill values
- Sliding window event tracking for recent player performance
- In game DDA debug panel with live graphs, event counters, parameter sliders, minimise control, and restart button
- Optional performance logging to CSV

## Built With

- Unity 6.3.6f1
- C#
- Unity 2D Physics
- Unity UI
- TextMeshPro
- Unity Sprite Shape

## Requirements

- Windows, macOS, or Linux
- Unity Hub
- Unity Editor `6000.3.6f1` or a compatible Unity 6 version

## How to Run

1. Clone or download this repository.
2. Open Unity Hub.
3. Select **Add** and choose the project folder.
4. Open the project using Unity `6000.3.6f1`.
5. Open `Assets/Scenes/SampleScene`.
6. Press the Play button in the Unity Editor.

## Controls

| Control | Action |
| --- | --- |
| `Space` | Jump while grounded |
| Hold `Space` in the air | Perform or continue a backflip |
| DDA panel arrow button | Minimise or expand the DDA debug panel |
| Restart button | Reload the current scene and restart the session |
| DDA sliders | Adjust DDA parameters during play |

## Dynamic Difficulty Adjustment

The game uses fuzzy logic to evaluate three inputs:

| Input | Description |
| --- | --- |
| Ground Skill (`Sg`) | Calculated from recent near misses and collisions with obstacles |
| Aerial Skill (`Sa`) | Calculated from completed backflips over a recent time window |
| Time Period (`Tp`) | Represents progression through the current play session |

These inputs are fuzzified into low, medium, and high membership values. A 27-rule fuzzy rule table then produces three outputs:

| Output | Effect |
| --- | --- |
| Speed change | Increases or decreases the player’s running speed |
| Obstacle density change | Adjusts the chance of obstacles spawning |
| Slide density change | Adjusts the frequency of small, medium, and big slide terrain |

The final fuzzy outputs are defuzzified into crisp values and clamped within configurable safe ranges.

## Debug Interface

The in game DDA panel displays:

- Current player speed
- Obstacle spawn chance
- Small, medium, and big slide weights
- Ground skill (`Sg`)
- Aerial skill (`Sa`)
- Recent near miss count
- Recent backflip count
- Fuzzy output labels for speed, obstacles, and slides
- Live graphs for gameplay and DDA values
- Runtime sliders for tuning DDA settings

## Technical Highlights

- Terrain is generated as connected chunks using curve-based surface geometry.
- Medium and big slides span multiple terrain chunks, followed by a recovery phase to maintain playable terrain.
- Obstacles are placed along valid terrain surfaces and aligned to the local slope.
- The player uses raycasts and 2D physics to remain attached to sloped terrain.
- The DDA controller tracks player events within a recent sliding time window.
- Ground and aerial skill values are smoothed using exponential moving averages.
- A custom graph renderer draws real time performance and DDA data directly to UI textures.
- Performance metrics can be exported to `PerformanceLog.csv`, including FPS, CPU frame time, and allocated memory usage.
