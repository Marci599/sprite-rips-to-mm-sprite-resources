## About

Created by Marci599 for Mario Multiverse.<br/>
Mario Multiverse (SFMB) created by neoarc.

This tool can be used to automatically process and generate spritesheet resources from individual recorded / rendered frames.

**Before using it, make sure to familiarize yourself with the game’s theme creation process, as this will help you understand how sprite sheet resources work.**

Let's say you want to use sprites from existing 3D games (like NSMB, NSMBWii or NSMBU), or if you want to implement your own 3D renders into MM, this tool can significantly speed up the implementation process.

**Download** latest version here: [Releases](https://github.com/Marci599/sprite-rips-to-mm-sprite-resources/releases)

<table>
  <tr>
    <td width="50%"><img width="100%" src="https://github.com/user-attachments/assets/c4824c3a-ad3f-4cbb-b7c5-c144a999f95c" /></td>
    <td width="50%"><img width="100%" src="https://github.com/user-attachments/assets/5282a0bc-b22e-4fd9-ab27-02055f620c5f" /></td>
  </tr>
      <tr>
    <td width="50%"><img width="100%" alt="image" src="https://github.com/user-attachments/assets/9984b262-b8e2-4ba0-ae0b-264bebcca090" /></td>
 <td width="50%"><img width="100%" alt="image" src="https://github.com/user-attachments/assets/b5f8b432-7cf2-4611-b1d6-3bb516f64630" /></td>
  </tr>
</table>

## How to use

### Setup

1. After installing the program, launch it and set the `Working directory path` to the directory where your folders and files will be located. (for example create a new empty folder inside your Mario Multiverse folder named `FTMMSRAssets` and set the path in the program to that folder.)

2. Press `Generate hierarchy` and an example directory will be generated inside your folder.

3. Rename your folders, create new ones, or remove them accordingly, then put your frames inside the animation folders. Make sure that their names include ordering numbers (for example: `frame001.png`, `frame002.png`, ...)
    
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
<img width="100%" alt="image" src="https://github.com/user-attachments/assets/928df885-ec49-4122-9cc5-176b735407da" />

You can download this example that contains a subject with raw frames: [V_Yoshi](https://github.com/Marci599/sprite-rips-to-mm-sprite-resources/blob/master/example.zip)
- Unzip it and put V_Yoshi inside your assets folder.
- Note that the Jump animation is just a placeholder.

### Understand how this program manages your files
Right now, when the window gets unfocused, it saves everything, and the next time the inside of the window gets clicked, it reloads everything. This can get a little slow with big projects, and it creates another issue as well: If there is a copying (or any file / folder related) process in the background that finishes while the program is focused, it's not going to detect those changes until the program gets unfocused, and clicked inside again. This is something I want to change in the future.

### Watermark

To automatically add your own watermark to the spritesheets, open your `Local App Data Directory` and put your watermark there named `WaterMark.png`.
- Watermarks only appear if the spritesheet contains more than 20 frames.

To remove the built in watermark, contact me.

### Controls

#### Tree view

- <b>Select:</b> `LMB`
- <b>Open in file explorer:</b> `RMB`
- <b>Multiselect:</b> Hold `CTRL`
- <b>Select all sibling:</b> `CTRL` + `A`
- <b>Change selected sibling:</b> `Q` `D`
  
You can select a range of siblings by holding `CTRL` and `Q` or `D`

#### Frame offset editor

- <b>Move canvas:</b> `RMB`
- <b>Zoom canvas:</b> Scroll `MMB`
- <b>Move frame:</b> `W` `A` `S` `D` (+`SHIFT` for added speed) / `LMB`
- <b>Toggle previous frame:</b> `R`

### Generating

Select a subject, and press `Generate <SubjectName>` to create the spritesheet resources into your output or assets.

<img width="100%" alt="gifftmmsr2" src="https://github.com/user-attachments/assets/3f8b2c8e-5b6c-47f8-8475-4dc9be970872" />
