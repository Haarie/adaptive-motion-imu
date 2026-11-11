# Builds docs/enclosure_mounting.png from build/mounted.png (part "mounted"):
#   openscad -D part=\"mounted\" --imgsize=1400,1100 --camera=0,-20,0,65,0,30,330 --colorscheme=Tomorrow -o build/mounted.png adaptive_motion_imu_enclosure.scad
from PIL import Image, ImageDraw, ImageFont
F="/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"; FB="/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
f=lambda s,b=False: ImageFont.truetype(FB if b else F, s)
img=Image.open("build/mounted.png").convert("RGB").crop((250,120,1350,1000))
W=1600; ink=(40,44,52)
notes=["Box across a vertical 40 x 40 (slot 8) profile; both M6 bolts go into the same slot",
       "Hardware: 2 x M6 x 16 DIN 912 + washer, 2 x M6 slot-8 T-nuts (drop-in or slide-in)",
       "Bolt pitch 49.4 mm along the profile, +-3 mm play; plate 34 x 67 mm, 6 mm thick + gussets",
       "Any angle works: run Auto-align in the plugin (rest, then nose-up) - FWD arrow is only a hint",
       "Mount it on the moving seat frame, not the base frame; USB lead with a cable tie on the tab"]
H=90+img.height+30+len(notes)*36+30
out=Image.new("RGB",(W,H),(248,248,248)); d=ImageDraw.Draw(out)
d.text((40,24),"Adaptive Motion IMU - mounting on a vertical profile (M6)",fill=ink,font=f(34,True))
out.paste(img,((W-img.width)//2,90))
y=90+img.height+30
for i,n in enumerate(notes): d.text((60,y+i*36),"- "+n,fill=ink,font=f(23))
out.save("../docs/enclosure_mounting.png"); print(out.size)
