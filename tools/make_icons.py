from PIL import Image, ImageDraw, ImageFont
import sys
F="/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf"
def icon(path, top, bottom, accent):
    im=Image.new("RGB",(256,256),(14,14,18)); d=ImageDraw.Draw(im)
    # скан-линии
    for y in range(0,256,4): d.line([(0,y),(255,y)],fill=(20,20,26))
    d.rectangle([8,8,247,247],outline=accent,width=4)
    f1=ImageFont.truetype(F,96); f2=ImageFont.truetype(F,40)
    def center(t,f,y,c):
        w=d.textlength(t,font=f); d.text(((256-w)/2,y),t,font=f,fill=c)
    center(top,f1,40,accent)
    center(bottom,f2,170,(230,230,235))
    im.save(path)
icon(sys.argv[1]+"/QoL/icon.png","GP","QoL",(255,79,216))
icon(sys.argv[1]+"/Rules/icon.png","GP","RULES",(255,170,40))
