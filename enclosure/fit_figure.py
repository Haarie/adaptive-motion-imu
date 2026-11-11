# Builds docs/enclosure_fit.png. Run from enclosure/ after rendering:
#   mkdir -p build
#   openscad -D part=\"exploded\" --imgsize=1600,1000 --camera=50,14,25,62,0,32,250 --colorscheme=Tomorrow -o build/exploded.png adaptive_motion_imu_enclosure.scad
#   openscad -D part=\"fit\" --imgsize=1600,700 --camera=49,14,0,0,0,0,170 --projection=o --colorscheme=Tomorrow -o build/top.png adaptive_motion_imu_enclosure.scad
#   python3 fit_figure.py
from PIL import Image, ImageDraw, ImageFont
F="/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"; FB="/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
f=lambda s,b=False: ImageFont.truetype(FB if b else F, s)
ex=Image.open("build/exploded.png").convert("RGB"); top=Image.open("build/top.png").convert("RGB")
W=1600
def px(x,y): return (806+(x-49.25)*10.23, 350-(y-13.7)*10.23)
ink=(40,44,52); acc=(230,120,20)
# --- annotate top view ---------------------------------------------------------------
d=ImageDraw.Draw(top)
calls=[ # n, target mm, label anchor px, side
 (1,(27.8,13.7),(560,70)),
 (2,(3.6,13.7),(150,70)),
 (3,(67,13.7),(930,70)),
 (4,(87.7,13.7),(1180,70)),
 (5,(19.7,-3.0),(420,640)),
 (6,(-9,13.7),(150,640)),
 (7,(-3.0,6.3),(280,640)),
 (8,(73,4.0),(860,640)),
]
for n,t,(lx,ly) in calls:
    tx,ty=px(*t)
    d.line([(lx,ly),(tx,ty)],fill=acc,width=3)
    d.ellipse([tx-6,ty-6,tx+6,ty+6],fill=acc)
    d.ellipse([lx-22,ly-22,lx+22,ly+22],fill=acc,outline="white",width=3)
    w=d.textlength(str(n),font=f(24,True)); d.text((lx-w/2,ly-15),str(n),fill="white",font=f(24,True))
# --- compose ---------------------------------------------------------------------------
exs=ex.resize((1440,900)).crop((0,20,1440,880))
legend=[
 "1  Arduino Micro: sits on two edge ledges between rails, pinned by two ribs under the lid",
 "2  Micro-USB socket against the -X end wall; the plug goes in through the wall cutout",
 "3  GY-521 (MPU-6050): two M2.5 x 6 screws into the bosses, header edge on the support bar",
 "4  BSS138 level shifter: stuck in its bay with VHB tape",
 "5  Lid: 4 x M3 screws into heat-set inserts in the outside lugs",
 "6  M5 slots for extrusion T-nuts (2 x, 114.5 mm apart)",
 "7  Cable-tie slots: USB strain relief on the flat ear",
 "8  Wires (5V, 3V3, GND, SDA, SCL) under 10 cm, twisted pairs, soldered - see the wiring sheet",
]
H=110+exs.height+60+top.height+40+len(legend)*34+30
out=Image.new("RGB",(W,H),(248,248,248)); D=ImageDraw.Draw(out)
D.text((40,22),"Adaptive Motion IMU - how the parts fit the case",fill=ink,font=f(34,True))
D.text((40,70),"Exploded view",fill=(110,110,110),font=f(22))
out.paste(exs,((W-exs.width)//2,105))
y=105+exs.height+20
D.text((40,y),"Top view, lid off (FWD = to the right, front of the rig)",fill=(110,110,110),font=f(22))
out.paste(top.crop((0,30,W,top.height)),(0,y+30)); y+=top.height+40
for i,l in enumerate(legend): D.text((60,y+i*34),l,fill=ink,font=f(22))
out.save("../docs/enclosure_fit.png")
print(out.size)
