#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Created on Tue Sep  7 15:10:21 2021

@author: chunlongyu
"""
import os
import sys
import numpy as np
import random 

def readInstance(path):
    ## ==== Read data =====
    
    #abspath = os.path.dirname(os.path.realpath(sys.argv[0])) 
    #os.chdir(abspath)
    
    #filePath = abspath+"/TestInstances/ht1_1.txt"   
    #filePath = abspath+ path 
    filePath = path
    lines = []
    with open(filePath) as f:
        lines = f.readlines()
        
    ## ==== Parameters ====
    count = 0
    numbers1 = [int(x) for x in lines[count].split() ] 
    types_mac = numbers1[0]
    types_parts = numbers1[1]
    
    count = 1
    numbers2 = [int(x) for x in lines[count].split() ] 
    num_mac = numbers2[0]
    num_parts = numbers2[1]
    
    V = [0 for i in range(num_mac)]
    U = [0 for i in range(num_mac)]
    S = [0 for i in range(num_mac)]
    L = [0 for i in range(num_mac)]
    W = [0 for i in range(num_mac)]
    HM = [0 for i in range(num_mac)]
    
    v  = [0 for i in range(num_parts)]
    Kj = dict()
    h  = dict()
    l  = dict()
    w  = dict()
    s  = dict()
    
    
    ##  ====== Machines ====== 
    count = 3 ## 
    count_mac = 0
    for i in range(types_mac):
        [index, num_mac_type, V[count_mac], U[count_mac],S[count_mac],\
         L[count_mac],W[count_mac],HM[count_mac] ] = [float(x) for x in lines[count].split() ] 
        
        if num_mac_type > 1:
            for j in range(num_mac_type):
                count_mac = count_mac +1
                [V[count_mac], U[count_mac],S[count_mac],L[count_mac],W[count_mac],HM[count_mac] ] = \
                [V[count_mac-1], U[count_mac-1],S[count_mac-1],L[count_mac-1],W[count_mac-1],HM[count_mac-1] ]
        
        count_mac = count_mac +1
        count = count + 1
    
    ## ====== Parts ======
    count_part = 0
    for i in range(types_parts):
        count = count + 1
    
        [index, num_part_type, num_ori, vol] = [float(x) for x in lines[count].split() ] 
        num_ori = int(num_ori)
        num_part_type = int(num_part_type)
        
        v[count_part] = vol
        
        for ii in range(int(num_ori)):
            Kj[count_part,ii] = ii
            
        for k in range(num_ori):
            count = count + 1
            [ll, ww, hh, ss] = [float(x) for x in lines[count].split() ] 
            l[count_part,k] = ll
            w[count_part,k] = ww
            h[count_part,k] = hh
            s[count_part,k] = ss
        count_part = count_part + 1
        
        if num_part_type > 1:
            for j in range(num_part_type -1):
                
                v[count_part] = vol
                for ii in range(int(num_ori)):
                    Kj[count_part,ii] = ii
                
                for k in range(num_ori):
                    l[count_part,k] = l[count_part-1,k] 
                    w[count_part,k] = w[count_part-1,k]
                    h[count_part,k] = h[count_part-1,k]
                    s[count_part,k] = s[count_part-1,k]    
                count_part = count_part +1
    
        count = count + 1
    
    
    return types_mac,types_parts,num_mac,num_parts, V,U,S,L,W,HM,v,Kj,h,l,w,s

def FormBatch(J, AP, AM):
    # Given a set of parts(represented by projection area) and machine area
    # , this function returns a set of formed batches in the format of dictionary
    # ==== Inputs ====
    # J: index of the parts to be batched 
    # AP: Area of all parts 
    # AM: Area of the machine
    
    B = dict()
    
    b = 0
    tot_area = 0
    jobs = []
    
    for j in J:
        done = False
        while not(done):
            if tot_area + AP[j] < AM:
                # assign the part to batch b
                jobs.append(j)
                tot_area = tot_area + AP[j]
                done = True
            else:
                #close the batch
                B[b] = jobs
                # start a new batch
                jobs = []
                tot_area = 0
                b = b+1
    # close the last batch
    B[b] = jobs    
    
    return B

def CalBatchProTime(B,i,HP,S,U,V,v,VS):
    # Given a set of batch and the corresponding machine id, calculate the processing time
    ProTime = 0
    for b in range(len(B)):
        ProTime =  ProTime + S[i] + V[i]*( sum( [v[j] for j in B[b] ]) + sum( [VS[j] for j in B[b] ] ) ) + U[i]*max( [ HP[j] for j in B[b]]  )
    
    return ProTime

def GenerateDueDate(path, TF, RDD, RndSeed):
    
    # ==== Inputs ====
    # TF: Tightness factor
    # RDD: Range of duedate
    # RndSeed: Random seed used to generate duedates
    random.seed(RndSeed)
    types_mac,types_parts,num_mac,num_parts, V,U,S,L,W,HM,v,Kj,h,l,w,s =  readInstance(path)
    
    # Estimate the exptected makespan
    AM = [L[i]*W[i] for i in range(num_mac)]      # Area of machine
    
    I = range(num_mac)
    J = range(num_parts)
    
    AP = []                                       # Area of parts 
    HP = []                                       # Height of parts
    VS = []                                       # Volume of support for the selected orientation
    K = []                                        # Selected orientation index
    D = []                                        # Duedate of parts
    
    for j in range(num_parts):
        hs = [ h[jj,kk] for jj,kk in Kj if jj ==j ]
        k = hs.index(max(hs))
        #k = hs.index(min(hs))             # i modified the way to estimate the makespan for tighter duedates
        K.append(k)
        VS.append( s[j,k]  )
        HP.append(max(hs))
        #HP.append(min(hs))
        AP.append( l[j,k]*w[j,k] )
        D.append(0)
        
    AP_vals = np.array(AP)
    J_sorted = np.argsort(-1*AP_vals)         # Job sequence with non-increasing area
    AP_sorted = AP_vals[J_sorted]             # sorted area of parts 
    
    # Initiate the batches on machine
    POM = dict()   # parts on machine
    AAM = dict()   # available area on machine
    MCT = dict()   # Machine completion time
    
    for i in range(num_mac):
        POM[i] = []
        AAM[i] = AM[i]
        MCT[i] = 0 
        
    # Assign parts to batches
    
    for j in J_sorted:
        MECT = [0 for i in I]         # Machine expected completion time
        for i in I:
            Temp = POM[i] + [j] 
            B = FormBatch( Temp, AP, AM[i])
            ProcT = CalBatchProTime(B,i,HP,S,U,V,v,VS)
            MECT[i] = ProcT
        
        ii = [i for i in I if MECT[i] == min(MECT) ]
        ii = ii[0]      # Selected machine
        
        # Update parts on machine & machine completion time
        POM[ii].append(j)
        MCT[ii] = MECT[ii]
    
    Cmax = max( [MCT[i] for i in MCT] )
    
    # Generate the duedates for jobs
    D = []
    for j in J:
        ddl = random.randint( int( max(1, Cmax*(1-TF-RDD/2)) ), int(Cmax*(1-TF+RDD/2)) )
        D.append( ddl )
    
    return D


class Instance:
    name = " "
    types_mac = 0
    types_parts = 0
    num_mac = 0
    num_parts = 0
    V = []
    U = []
    S = []
    L = []
    W = []
    HM = []
    v = []
    d = []
    Kj = dict()
    h = dict()
    l = dict()
    w = dict()
    s = dict()
    
    # TF = 0.3
    # RDD = 0.6
    # RndSeed = 1
    
    def __init__(self, name_):
        self.name = name_
        
    def load(self,filePath):
        self.types_mac, self.types_parts,self.num_mac,self.num_parts,self.V,self.U,\
            self.S,self.L,self.W,self.HM,self.v,self.Kj,self.h,self.l,self.w,self.s = readInstance(filePath)
            
    def GenerateDD(self,filePath, TF,RDD,RndSeed):
        d = GenerateDueDate(filePath, TF, RDD, RndSeed)
        self.d = d
        return d

        
