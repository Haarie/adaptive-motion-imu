# Builds docs/enclosure_mounting.png from build/mounted.png (part "mounted"):
#   openscad -D part=\"mounted\" --imgsize=1400,1100 --camera=0,-20,0,70,0,25,300 --colorscheme=Tomorrow -o build/mounted.png adaptive_motion_imu_enclosure.scad
from PIL import Image, ImageDraw, ImageFont
F="/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"; FB="/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
f=lambda s,b=False: ImageFont.truetype(FB if b else F, s)
img=Image.open("build/mounted.png").convert("RGB").crop((0,40,1400,720))
W=1600; ink=(40,44,52)
notes=["Box along a horizontal 30/40 profile, on its vertical side face; both M6 bolts in that face's horizontal slot",
       "Hardware: 2 x M6 x 16 DIN 912 + washer, 2 x M6 slot-8 T-nuts (drop-in or slide-in)",
       "Bolt pitch 32 mm, +-3 mm play; 6 mm flange + 3 gussets; box sits beside the slot (above or below, flip it)",
       "Lid faces the rig front (mount on the rear face: lid faces rear). Run Auto-align either way",
       "Mount it on the moving seat frame, not the base frame; USB lead with a cable tie on the tab"]
H=90+img.height+30+len(notes)*36+30
out=Image.new("RGB",(W,H),(248,248,248)); d=ImageDraw.Draw(out)
d.text((40,24),"Adaptive Motion IMU - mounting on a horizontal cross-member (M6)",fill=ink,font=f(34,True))
out.paste(img,((W-img.width)//2,90))
y=90+img.height+30
for i,n in enumerate(notes): d.text((60,y+i*36),"- "+n,fill=ink,font=f(23))
out.save("../docs/enclosure_mounting.png"); print(out.size)
