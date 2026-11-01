"""시안의 명암·노멀과 옆머리의 곡선 좌표를 원본 UV에 베이크한다."""
from pathlib import Path
import numpy as np
from PIL import Image,ImageFilter
import struct,time
import argparse

ASSETS=Path(__file__).resolve().parents[4]
SOURCE=ASSETS/'Arts/Models/YPH/FinalPlayer3/HairAuthoring~'
parser=argparse.ArgumentParser(description='시안의 머리결을 원본 UV에 베이크합니다. numpy와 Pillow가 필요합니다.')
parser.add_argument('--output',type=Path,default=ASSETS/'Arts/Models/YPH/FinalPlayer3/Textures',help='두 PNG를 저장할 폴더')
ROOT=parser.parse_args().output.resolve()
ROOT.mkdir(parents=True,exist_ok=True)
RES=4096

def unit(x):return x/np.maximum(np.linalg.norm(x,axis=-1,keepdims=True),1e-8)
def blur(a,r):
    return np.asarray(Image.fromarray(np.uint8(np.clip(a,0,1)*255)).filter(ImageFilter.GaussianBlur(r)),dtype=np.float32)/255
def sample(im,u,v):
    height,width=im.shape[:2];x=np.clip(u,0,1)*(width-1);y=np.clip(v,0,1)*(height-1)
    x0=x.astype(int);y0=y.astype(int);x1=np.minimum(x0+1,width-1);y1=np.minimum(y0+1,height-1)
    fx=(x-x0)[:,None];fy=(y-y0)[:,None]
    return (im[y0,x0]*(1-fx)+im[y0,x1]*fx)*(1-fy)+(im[y1,x0]*(1-fx)+im[y1,x1]*fx)*fy

def source(path,crop,mask_path):
    rgb=np.asarray(Image.open(path).convert('RGB').crop(crop),dtype=np.float32)/255
    # 장식은 원본 이미지에서 이미 제거했다. 색으로 거르면 따뜻한 광택까지
    # 사라지므로, 머리 영역을 그린 별도 마스크로 피부와 배경만 제외한다.
    valid=np.asarray(Image.open(mask_path).convert('L'))>127
    if valid.shape!=rgb.shape[:2]:raise ValueError('머리 영역 마스크와 Crop 크기가 다릅니다.')
    if not valid.any():raise ValueError('머리 영역 마스크가 비어 있습니다.')
    lum=rgb@np.array([.2126,.7152,.0722],np.float32)
    # 제외한 영역을 주변 머리색으로 연장해 UV의 바깥쪽에 피부색이 번지지 않게 한다.
    for _ in range(sum(valid.shape)):
        if valid.all():break
        total=np.zeros_like(lum);count=np.zeros_like(lum)
        for dy,dx in ((0,1),(0,-1),(1,0),(-1,0)):
            other=np.roll(valid,(dy,dx),(0,1))
            # 반대쪽 테두리가 연결되지 않게 막아 가까운 머리색만 연장한다.
            if dy>0:other[:dy,:]=False
            if dy<0:other[dy:,:]=False
            if dx>0:other[:,:dx]=False
            if dx<0:other[:,dx:]=False
            total+=np.roll(lum,(dy,dx),(0,1))*other;count+=other
        fill=(~valid)&(count>0);lum[fill]=total[fill]/count[fill];valid[fill]=True
    low=blur(lum,9)
    high=np.log(lum+.018)-np.log(low+.018)
    detail=.5+.35*np.tanh(high*2.7)
    gy,gx=np.gradient(high)
    # 구조 텐서에서 결을 가로지르는 방향을 읽는다.
    def gaussian(a,r):
        radius=int(np.ceil(r*3));k=np.exp(-np.arange(-radius,radius+1,dtype=float)**2/(2*r*r));k/=k.sum()
        f=lambda row:np.convolve(np.pad(row,radius,mode='edge'),k,mode='valid')
        return np.apply_along_axis(f,0,np.apply_along_axis(f,1,a))
    xx=gaussian(gx*gx,4)
    yy=gaussian(gy*gy,4)
    xy=gaussian(gx*gy,4)
    theta=.5*np.arctan2(2*xy,xx-yy)+np.pi*.5
    confidence=np.clip(np.sqrt((xx-yy)**2+4*xy*xy)/(xx+yy+1e-5),0,1)
    packed=np.stack((detail,np.clip(gx*4,-.7,.7),np.clip(-gy*4,-.7,.7),np.cos(theta),-np.sin(theta),confidence,lum),axis=2)
    return packed

back=source(SOURCE/'HairReferenceBack.png',(150,220,1030,690),SOURCE/'HairReferenceBackMask.png')
side=source(SOURCE/'HairReferenceSide.png',(370,270,915,700),SOURCE/'HairReferenceSideMask.png')
# 사진 투영이 목을 읽는 옆머리는 3D 곡선 좌표와 무료 섬유 자료로 보완한다.
# 원본 메시·뼈·UV는 바꾸지 않으며 외부 자료의 출처는 LICENSE에 기록한다.
curl_fibers=np.asarray(Image.open(SOURCE/'CurlFiberSource.png').convert('RGBA'),dtype=np.float32)/255
with (SOURCE/'OriginalHairMesh.bin').open('rb') as f:
    count,k=struct.unpack('<II',f.read(8));vertices=np.fromfile(f,'<f4',count=count*12).reshape(-1,12);faces=np.fromfile(f,'<u4',count=k).reshape(-1,3)

# UV 조각과 관계없이 같은 3D 곡선 좌표를 사용한다.
hair_points=np.unique(np.round(vertices[np.unique(faces),:3],6),axis=0)
profile_z=np.linspace(3.29,3.63,80)
profiles=[]
for side_sign in (-1,1):
    points=hair_points[(hair_points[:,0]*side_sign>.045)&(hair_points[:,1]<.09)&(hair_points[:,2]<3.65)]
    rows=[]
    for z in profile_z:
        section=points[np.abs(points[:,2]-z)<.014]
        if len(section)<3:rows.append([np.nan]*4);continue
        low,high=np.quantile(section[:,:2],[.08,.92],axis=0)
        rows.append([*(.5*(low+high)),*np.maximum(.5*(high-low),.012)])
    rows=np.asarray(rows)
    for j in range(4):
        valid=np.isfinite(rows[:,j]);rows[:,j]=np.interp(profile_z,profile_z[valid],rows[valid,j]);kernel=np.exp(-np.arange(-4,5)**2/8);kernel/=kernel.sum();rows[:,j]=np.convolve(np.pad(rows[:,j],4,mode='edge'),kernel,mode='valid')
    dx=np.clip(np.gradient(rows[:,0],profile_z),-3,3);dy=np.clip(np.gradient(rows[:,1],profile_z),-3,3)
    profiles.append(np.column_stack((rows,dx,dy)))

def smooth(a,b,x):
    t=np.clip((x-a)/(b-a),0,1);return t*t*(3-2*t)

def curve_coordinates(p,n):
    arrays=[]
    for profile in profiles:arrays.append(np.column_stack([np.interp(p[:,2],profile_z,profile[:,i]) for i in range(6)]))
    pr=np.where((p[:,0]<0)[:,None],arrays[0],arrays[1]);cx,cy,rx,ry,dx,dy=pr.T
    px=(p[:,0]-cx)/rx;py=(p[:,1]-cy)/ry
    phase=np.arctan2(px,-py)/(2*np.pi)+.5
    along=np.clip((3.63-p[:,2])/.34,0,1)
    coordinate=np.stack((.25+.5*phase,.18+.62*along),axis=1)
    axis=unit(np.stack((dx,dy,np.ones(len(p))),axis=1));projected=axis-n*np.sum(axis*n,axis=1,keepdims=True)
    fiber=unit(np.where((np.linalg.norm(projected,axis=1)>1e-5)[:,None],projected,np.cross(n,np.array([1.,0.,0.]))))
    across=unit(np.cross(fiber,n));gradient=np.stack((-py/rx,px/ry,np.zeros(len(p))),axis=1);across*=np.where(np.sum(across*gradient,axis=1,keepdims=True)<0,-1,1)
    mask=(1-smooth(3.55,3.64,p[:,2]))*(1-smooth(.035,.10,p[:,1]))*smooth(.045,.08,np.abs(p[:,0]))
    return coordinate,fiber,across,mask

surface=np.empty((RES,RES,4),np.uint8);surface[:]=[128,128,128,128]
flow=np.empty_like(surface);flow[:]=[128,128,128,0]
coordinates=np.full((RES,RES,2),.5,np.float32)
covered=np.zeros((RES,RES),bool);start=time.monotonic()
def byte(x):return np.uint8(np.rint(np.clip(x,0,1)*255))
for ids in faces:
    uv=vertices[ids,10:12];q=np.stack((uv[:,0]*RES,(1-uv[:,1])*RES),axis=1)
    lo=np.maximum(np.floor(q.min(0)).astype(int),0);hi=np.minimum(np.ceil(q.max(0)).astype(int),RES-1);x0,y0=lo;x1,y1=hi
    if x0>x1 or y0>y1:continue
    a,b,c=q;den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
    if abs(den)<1e-8:continue
    xx,yy=np.meshgrid(np.arange(x0,x1+1,dtype=np.float32)+.5,np.arange(y0,y1+1,dtype=np.float32)+.5)
    wa=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/den;wb=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/den;wc=1-wa-wb
    ys,xs=slice(y0,y1+1),slice(x0,x1+1);inside=(wa>=-1e-5)&(wb>=-1e-5)&(wc>=-1e-5)&~covered[ys,xs]
    if not inside.any():continue
    weights=np.stack((wa[inside],wb[inside],wc[inside]),axis=1);p=weights@vertices[ids,:3];n=unit(weights@vertices[ids,3:6]);t=weights@vertices[ids,6:10]
    tx=unit(t[:,:3]-n*np.sum(n*t[:,:3],axis=1,keepdims=True));ty=unit(np.cross(n,tx))*np.where(t[:,3:4]<0,-1,1)
    back_sample=sample(back,(p[:,0]+.30)/.60,(3.78-p[:,2])/.49)
    side_sample=sample(side,(p[:,1]+.22)/.59,(3.78-p[:,2])/.49)
    wb_=np.maximum(n[:,1],0)**4;ws_=np.abs(n[:,0])**4+np.maximum(-n[:,1],0)**4;weight=(wb_/(wb_+ws_+1e-6))[:,None]
    detail=back_sample[:,0:1]*weight+side_sample[:,0:1]*(1-weight)
    raw=back_sample[:,6:7]*weight+side_sample[:,6:7]*(1-weight)
    delta_back=np.stack((back_sample[:,1],np.zeros(len(p)),back_sample[:,2]),axis=1)
    delta_side=np.stack((np.zeros(len(p)),side_sample[:,1],side_sample[:,2]),axis=1)
    delta=(delta_back*weight+delta_side*(1-weight))*.48
    nx=np.sum(delta*tx,axis=1);ny=np.sum(delta*ty,axis=1)
    back_f=np.stack((back_sample[:,3],np.zeros(len(p)),back_sample[:,4]),axis=1)
    side_f=np.stack((np.zeros(len(p)),side_sample[:,3],side_sample[:,4]),axis=1)
    back_f=unit(back_f-n*np.sum(back_f*n,axis=1,keepdims=True));side_f=unit(side_f-n*np.sum(side_f*n,axis=1,keepdims=True))
    sign=np.where(np.sum(back_f*side_f,axis=1,keepdims=True)<0,-1,1);f=unit(back_f*weight+side_f*sign*(1-weight))
    fx=np.sum(f*tx,axis=1);fy=np.sum(f*ty,axis=1)
    coordinate,curl_fiber,curl_across,curl_mask=curve_coordinates(p,n)
    fibers=sample(curl_fibers,coordinate[:,0],coordinate[:,1])
    curl_delta=(curl_across*(fibers[:,0:1]*2-1)-curl_fiber*(fibers[:,1:2]*2-1))*.35
    nx=nx*(1-curl_mask)+np.sum(curl_delta*tx,axis=1)*curl_mask
    ny=ny*(1-curl_mask)+np.sum(curl_delta*ty,axis=1)*curl_mask
    sign=np.where(np.sum(f*curl_fiber,axis=1,keepdims=True)<0,-1,1)
    f=unit(f*(1-curl_mask[:,None])+curl_fiber*sign*curl_mask[:,None])
    fx=np.sum(f*tx,axis=1);fy=np.sum(f*ty,axis=1)
    coordinates[ys,xs][inside]=coordinate
    surface[ys,xs][inside]=np.stack((byte(nx*.5+.5),byte(ny*.5+.5),byte(raw[:,0]),byte(detail[:,0])),axis=1)
    flow[ys,xs][inside]=np.stack((byte((fx*fx-fy*fy)*.5+.5),byte(fx*fy+.5),np.full(len(p),230,np.uint8),byte(curl_mask)),axis=1)
    covered[ys,xs][inside]=True
for _ in range(14):
    old=covered.copy()
    for dy,dx in ((0,1),(0,-1),(1,0),(-1,0)):
        sy=slice(max(0,dy),min(RES,RES+dy));dy_=slice(max(0,-dy),min(RES,RES-dy));sx=slice(max(0,dx),min(RES,RES+dx));dx_=slice(max(0,-dx),min(RES,RES-dx));take=(~covered[dy_,dx_])&old[sy,sx]
        for im in (surface,flow,coordinates):im[dy_,dx_][take]=im[sy,sx][take]
        covered[dy_,dx_][take]=True
# RGBA는 색상이 아닌 데이터이므로 Flow를 채널별로 축소한다.
# 일반 RGBA 축소는 알파가 0인 뒷머리의 방향 값까지 지울 수 있다.
Image.fromarray(surface).save(ROOT/'YPH_HairReferenceSurface.png');Image.fromarray(np.stack([np.asarray(Image.fromarray(flow[:,:,i]).resize((1024,1024),Image.Resampling.BOX)) for i in range(4)],axis=2)).save(ROOT/'YPH_HairReferenceFlow.png')
print('머리결 베이크 저장 완료:',round(time.monotonic()-start,1),'초',flush=True)

# 좌표는 축소한 뒤 16비트 U/V로 포장한다. Unity에서는 압축과 Mipmap을 끈다.
channels=[np.asarray(Image.fromarray(coordinates[:,:,i]).resize((1024,1024),Image.Resampling.BILINEAR)) for i in range(2)]
u=np.rint(np.clip(channels[0],0,1)*65535).astype(np.uint32);v=np.rint(np.clip(channels[1],0,1)*65535).astype(np.uint32)
Image.fromarray(np.stack((u//256,u%256,v//256,v%256),axis=2).astype(np.uint8)).save(ROOT/'YPH_HairCurveCoordinates.png')
print('옆머리 곡선 좌표 저장 완료',flush=True)
