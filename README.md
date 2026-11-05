# Tower Defence

A 3D tower defence game made in Unity: place turrets on build nodes, earn money for kills, and stop enemy waves before they drain your lives.

![Gameplay](docs/demo.gif)

> TODO: record `docs/demo.gif` (one full wave, 10-15 s) once the game is finished.

## Features

- Build nodes and a shop (`BuildManager`, `Node`, `Shop`, `TurretBlueprint`)
- Turrets that aim and shoot (`Turret`, `AimSelector`, `Bullet`)
- Enemies following a path (`Enemies`, `EnemyMovement`)
- Money and lives UI, pause and game over screens (`PlayerStats`, `MoneyUI`, `LifeUI`, `Paused`, `GameOver`)
- TODO: waves, turret upgrades, win condition (update after finishing)

## Play

TODO: link to the Windows/WebGL build in Releases.

## Run from source

1. Install Unity TODO version (see `ProjectSettings/ProjectVersion.txt` once the full project is committed).
2. Clone the repo and open it in Unity Hub.
3. Open `Assets/Scenes/GameScene.unity` and press Play.

## Controls

TODO

## Project layout

```
Assets/Scripts/   game scripts
Assets/Prefabs/   turrets, enemies, bullets
docs/             demo.gif
```

## License

MIT, see [LICENSE](LICENSE).
