from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).parent
s = 4
im = Image.new('RGBA', (256*s, 256*s), (0,0,0,0))
d = ImageDraw.Draw(im)
def box(rect, radius, fill):
    d.rounded_rectangle(tuple(int(x*s) for x in rect), radius=radius*s, fill=fill)
def line(points, fill, width):
    d.line([(int(x*s),int(y*s)) for x,y in points], fill=fill, width=width*s, joint='curve')
    for x,y in [points[0],points[-1]]:
        r=width*s/2
        d.ellipse((x*s-r,y*s-r,x*s+r,y*s+r),fill=fill)
box((8,8,248,248),52,'#1b2130')
box((59,42,196,217),16,'#edf4f4')
box((75,29,89,60),6,'#91d0ad')
box((163,29,177,60),6,'#91d0ad')
line([(85,86),(167,86)],'#a6b7c1',9)
line([(85,112),(143,112)],'#a6b7c1',9)
box((126,134,239,239),37,'#91d0ad')
line([(152,184),(173,205),(214,164)],'#1b2130',13)
im=im.resize((256,256),Image.Resampling.LANCZOS)
im.save(root/'daytasks.png')
im.save(root/'daytasks.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
with Image.open(root/'daytasks.ico') as icon:
    assert len(icon.ico.sizes()) == 7
    print('Icon ready: 7 sizes, 16–256 px')
