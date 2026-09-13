1 REM Inspired by humanshader.com
10 BORDER 0: PAPER 7: CLS
100 LET row=0: LET col=0
110 DIM i(64): LET lum=0
111 LET y=175-row*8: LET x=col*8: GO SUB 1000: GO SUB 8040: LET c1= FN i(r,g,b)
112 FOR y=175-row*8-7 TO 175-row*8 STEP 3: FOR x=col*8+1 TO col*8+7 STEP 3: GO SUB 1000: GO SUB 8040: IF FN i(r,g,b) <> c1 THEN GO TO 120
113 NEXT x: NEXT y
115 LET px=col*8: LET py=175-row*8: LET c2=c1: LET lum=(r+g+b)/3: GO TO 246
120 FOR y=0 TO 7: FOR x=0 TO 7
130 LET px=col*8+x: LET py=175-(row*8+y)
140 LET ox=x: LET oy=y: LET x=px: LET y=py
150 GO SUB 1000: LET x=ox: LET y=oy: GO SUB 8040
160 LET i(y*8+x+1)= FN i(r,g,b)
165 LET lum=lum+r+g+b
170 NEXT x: NEXT y
175 LET lum=lum/(3*64)
180 DIM c(8): FOR n=1 TO 64: LET c(i(n)+1)=c(i(n)+1)+1: NEXT n
200 LET mx=0: LET c1=0: FOR n=1 TO 8: IF c(n)>mx THEN LET mx=c(n): LET c1=n-1
210 NEXT n
230 LET c(c1+1)=0
240 LET mx=0: LET c2=c1: FOR n=1 TO 8: IF c(n)>mx THEN LET mx=c(n): LET c2=n-1
245 NEXT n
246 LET br= INT (lum*1.2)
250 IF c1 <> c2 THEN GO TO 260
251 REM Solid
253 LET dd=0
254 FOR y=175-row*8-7 TO 175-row*8: FOR x=col*8 TO col*8+7
255 IF lum*1.7< RND THEN LET dd=1: PLOT BRIGHT br; PAPER c1; INK 0;x,y
256 NEXT x: NEXT y
257 IF dd=0 THEN PLOT BRIGHT br; PAPER c1; INK c1;x-1,y-1
259 GO TO 800
260 FOR y=0 TO 7: FOR x=0 TO 7
270 IF i(y*8+x+1)=c1 THEN GO TO 300
280 LET px=col*8+x: LET py=175-(row*8+y)
290 PLOT BRIGHT br; PAPER c1; INK c2;px,py
300 NEXT x: NEXT y
800 LET col=col+1: IF col<32 THEN GO TO 110
810 LET col=0
820 LET row=row+1: IF row<22 THEN GO TO 110
998 INK 0
999 GO TO 999
1000 REM Calc rgb for pixel x,y
1010 LET rx=71: LET ry=40
1020 LET i=rx*x/255: LET j=ry*y/175
1030 LET colr=0: LET colg=0: LET colb=0: LET cx=i: LET cy=39-j
1040 GO SUB 2000
1990 LET r=colr/255: LET g=colg/255: LET b=colb/255
1999 RETURN
2000 REM compute()
2010 REM sets colr,colg,colb
2020 LET u=cx-36
2030 LET v=18-cy
2040 LET u2=u*u
2050 LET v2=v*v
2060 LET h=u2+v2
2070 IF h<200 THEN GO SUB 3000: GO TO 2100
2080 IF v<0 THEN GO SUB 4000: GO TO 2100
2090 GO SUB 5000
2100 REM SectionE
2110 IF r>255 THEN LET r=255
2120 IF b>255 THEN LET b=255
2130 LET g= FN d(r*7+3*b,10)
2140 LET colr=r: LET colg=g: LET colb=b
3000 REM SectionB
3010 LET r=420
3020 LET b=520
3030 LET t=5000+h*8
3040 LET p= FN d(t*u,100)
3050 LET q= FN d(t*v,100)
3060 LET s=2*q
3070 LET w=8+ FN d(p-s+1000,100)
3080 IF w>0 THEN LET r=r+w*w
3090 LET o=s+2200
3100 LET r= FN d(r*o,10000)
3110 LET b= FN d(b*o,10000)
3120 IF p>-q THEN LET w= FN d(p+q,10): LET r=r+w: LET b=b+w
3999 RETURN
4000 REM SectionC
4010 LET r=150+2*v
4020 LET b=50
4030 LET p=h+8*v2
4040 LET c=-240*v-p
4050 IF c>1200 THEN LET o= FN d(6*c,10): LET o= FN d(c*(1500-o),100)-8360: LET r= FN d(r*o,1000): LET b= FN d(b*o,1000)
4060 LET rr=c+u*v
4070 LET d=3200-h-2*rr
4080 IF d>0 THEN LET r=r+d
4999 RETURN
5000 REM SectionD
5010 LET c=cx+4*cy
5020 LET r=132+c
5030 LET b=192+c
5031 LET b=255
5032 LET r=c
5999 RETURN
8000 REM Functions
8010 DEF FN d(a,b)= INT ( INT (a+b/2)/b)
8020 DEF FN c(x,y)= ATTR ( INT ((175)-y/8), INT (x/8))
8030 DEF FN i(r,g,b)= INT (g+0.5)*4+ INT (r+0.5)*2+ INT (b+0.5)
8040 LET dith=( RND -0.5)*0.06: LET r=0.1+0.84*r+dith: LET g=0.1+0.84*g+dith: LET b=0.1+0.84*b+dith: RETURN
