## About

Created by Marci599 for Mario Multiverse.<br/>
Mario Multiverse (SFMB) created by neoarc.

This tool can be used to automatically generate spritesheet resources from individual recorded frames.

**Before using it, make sure to familiarize yourself with the game’s theme creation process, as this will help you understand how sprite sheet resources work.**

Let's say you want to use sprites from existing 3D games (like NSMB, NSMBWii or NSMBU), or if you want to implement your own 3D renders into MM, this tool can significantly speed up the implementation process.

Download latest version here: [Releases](https://github.com/Marci599/sprite-rips-to-mm-sprite-resources/releases)

## How to use

**Make sure to put the program in its own folder**

If you are **not** planning to work with different Game Themes, structure your files as follows:
```
<Folder>
├─ FramesToMMSpriteResources.exe
├─ <SubjectName>
│  └─ raw
│     ├─ <AnimationName>
│     │  ├─ frame001.png
│     │  └─ frame002.png
│     ├─ <AnimationName>
│     └─ <AnimationName>
├─ <SubjectName>
└─ <SubjectName>
```

If you plan to work with different Game Themes, then structure your files as follows:
```
<Folder>
├─ FramesToMMSpriteResources.exe
├─ <GameThemeName>
│  ├─ <SubjectName>
│  │  └─ raw
│  │     ├─ <AnimationName>
│  │     │  ├─ frame001.png
│  │     │  └─ frame002.png
│  │     ├─ <AnimationName>
│  │     └─ <AnimationName>
│  ├─ <SubjectName>
│  └─ <SubjectName>
├─ <GameThemeName>
└─ <GameThemeName>
```

You can download this example that contains a subject with raw frames: [V_Yoshi](https://github.com/Marci599/sprite-rips-to-mm-sprite-resources/blob/master/example.zip)
- Unzip it and put V_Yoshi next to the pragram.
- Note that the Jump aniamtions is just a placeholder.

After setup, launch the program, configure options, and use `Generate Subject` to create the spritesheet resources into `<SubjectName>/generated`.

For best results, if your subject moves around in the raw recording and you want to resize it, adjust each raw frame in a separate program so it appears stationary before generating the spritesheet.
