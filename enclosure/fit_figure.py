# Builds docs/enclosure_fit.png. Run from enclosure/ after rendering:
#   mkdir -p build
#   openscad -D part=\"exploded\" --imgsize=1600,1000 --camera=34,14,30,62,0,32,215 --colorscheme=Tomorrow -o build/exploded.png adaptive_motion_imu_enclosure.scad
#   openscad -D part=\"fit\" --imgsize=1600,800 --camera=34.15,10,0,0,0,0,125 --projection=o --colorscheme=Tomorrow -o build/top.png adaptive_motion_imu_enclosure.scad
#   python3 fit_figure.py
from PIL import Image, ImageDraw, ImageFont
F="/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"; FB="/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
f=lambda s,b=False: ImageFont.truetype(FB if b else F, s)
ex=Image.open("build/exploded.png").convert("RGB"); top=Image.open("build/top.png").convert("RGB")
W=1600
CX,CY,DIST=34.15,10.0,125.0
S=9.97*170/DIST                      # px per mm for the orthographic top view
def px(x,y): return (top.width/2+(x-CX)*S, top.height/2-(y-CY)*S)
ink=(40,44,52); acc=(230,120,20)
# --- annotate top view ---------------------------------------------------------------
d=ImageDraw.Draw(top)
calls=[ # n, target mm, label px
 (1,(22.0,15.5),(640,75)),
 (2,(3.0,13.8),(150,75)),
 (3,(52.0,17.0),(1250,75)),
 (4,(40.5,8.6),(1400,330)),
 (5,(10.0,33.0),(300,75)),
 (6,(34.15,-11.0),(980,760)),
 (7,(-3.0,6.3),(80,520)),
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
 "1  ProMicro nRF52840 (USB-C): sits on two edge ledges between rails, pinned by two ribs under the lid",
 "2  USB-C socket against the -X end wall; the plug goes in through the 13 x 8 mm wall cutout",
 "3  GY-521 (MPU-6050): two M2.5 x 6 screws into the bosses, header edge on the support bar",
 "4  Four wires, board straight to GY-521 (no level shifter, all 3.3 V): VCC, GND, SDA, SCL",
 "5  Lid: 4 x M3 screws into heat-set inserts in the outside lugs",
 "6  Side flange: 2 x M6 slots, 32 mm apart (+-3 mm play), both in the horizontal slot of the beam",
 "7  Cable-tie slots: USB strain relief on the flat tab",
]
H=110+exs.height+60+top.height+40+len(legend)*34+30
out=Image.new("RGB",(W,H),(248,248,248)); D=ImageDraw.Draw(out)
D.text((40,22),"Adaptive Motion IMU - how the parts fit the case",fill=ink,font=f(34,True))
D.text((40,70),"Exploded view",fill=(110,110,110),font=f(22))
out.paste(exs,((W-exs.width)//2,105))
y=105+exs.height+20
D.text((40,y),"Top view, lid off",fill=(110,110,110),font=f(22))
out.paste(top.crop((0,30,W,top.height)),(0,y+30)); y+=top.height+40
for i,l in enumerate(legend): D.text((60,y+i*34),l,fill=ink,font=f(22))
out.save("../docs/enclosure_fit.png")
print(out.size)
