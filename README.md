## About

Created by Marci599 for Mario Multiverse.<br/>
Mario Multiverse (SFMB) created by neoarc.

This tool can be used to automatically generate spritesheet resources from individual recorded frames.

**Before using it, make sure to familiarize yourself with the game’s theme creation process, as this will help you understand how sprite sheet resources work.**

Let's say you want to use sprites from existing 3D games (like NSMB, NSMBWii or NSMBU), or if you want to implement your own 3D renders into MM, this tool can significantly speed up the implementation process.

Download latest version here: [Releases](https://github.com/Marci599/sprite-rips-to-mm-sprite-resources/releases)

## How to use

### Setup

1. After intsalling the program, launch it and set the `Working directory path` to the directory where your folders and files will be located. (for example create a new empty folder inside your Mario Multiverse folder named `FTMMSRAssets` and set the path in the program to that folder.)

2. Press `Generate hierarchy` and an example directory will be generated inside your folder.

3. Rename your folders, create new ones, or remove them accordingly, then put your frames inside the aniamtion folders. Make sure that their name has ordering numbers (for example: `frame001.png`, `frame002.png`, ...)
    
This is how your folders and files should look like.
```
<FTMMSRAssets>
├─ <SubjectName>
│  ├─ <AnimationName>
│  │  ├─ frame001.png
│  │  └─ frame002.png
│  ├─ <AnimationName>
│  └─ <AnimationName>
├─ <SubjectName>
└─ <SubjectName>
```

You can download this example that contains a subject with raw frames: [V_Yoshi](https://github.com/Marci599/sprite-rips-to-mm-sprite-resources/blob/master/example.zip)
- Unzip it and put V_Yoshi inside a GameTheme.
- Note that the Jump aniamtions is just a placeholder.

### Controls

#### Tree view

- <b>Select:</b> `LMB`
- <b>Open in file explorer:</b> `RMB`
- <b>Multiselect:</b> Hold `CTRL`
- <b>Select all sibling:</b> `CTRL` + `A`
- <b>Change selected sibling:</b> `Q` `D`

#### Frame offset editor

- <b>Move canvas:</b> `RMB`
- <b>Zoom canvas:</b> Scroll `MMB`
- <b>Move frame:</b> `W` `A` `S` `D` (+`SHIFT` for added speed) / `LMB`
- <b>Toggle previous frame:</b> `R`

### Generating

Select a subject, and press `Generate <SubjectName>` to create the spritesheet resources into `<SubjectName>/generated`.