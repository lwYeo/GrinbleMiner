// Cuckaroo Cycle, a memory-hard proof-of-work by John Tromp and team Grin
// Copyright (c) 2018 Jiri Photon Vadura and John Tromp
// Modified work Copyright (c) 2019 Lip Wee Yeo
// This Grinble miner file is covered by the FAIR MINING license

#pragma OPENCL EXTENSION cl_khr_int64_base_atomics : enable
#pragma OPENCL EXTENSION cl_khr_int64_extended_atomics : enable

typedef uint8 u8;
typedef uint16 u16;
typedef uint u32;
typedef ulong u64;

#define DUCK_SIZE_A 129
#define DUCK_SIZE_B 83

#define DUCK_A_EDGES (DUCK_SIZE_A * 1024)
#define DUCK_A_EDGES_64 (DUCK_A_EDGES * 64)

#define DUCK_B_EDGES (DUCK_SIZE_B * 1024)
#define DUCK_B_EDGES_64 (DUCK_B_EDGES * 64)

#define EDGE_BLOCK_SIZE (64)
#define EDGE_BLOCK_MASK (EDGE_BLOCK_SIZE - 1)

#define EDGEBITS 29
// number of edges
#define NEDGES (1u << EDGEBITS)
// used to mask siphash output
#define EDGEMASK (NEDGES - 1)

#define CTHREADS 1024
#define BKTMASK4K (4096-1)
#define BKTGRAN 32

static inline ulong RotateX(const ulong vw)
{
	const uint2 v = as_uint2(vw);
	return as_ulong((uint2)(v.y, v.x));
}

#define SIPROUND \
  { \
    v0 += v1; v2 += v3; v1 = rotate(v1, 13ul); \
    v3 = rotate(v3, 16ul); v1 ^= v0; v3 ^= v2; \
    v0 = RotateX(v0); v2 += v1; v0 += v3; \
    v1 = rotate(v1, 17ul); v3 = rotate(v3, 21ul); \
    v1 ^= v2; v3 ^= v0; v2 = RotateX(v2); \
  }

#define READ_2B_COUNTER(e,b) ( (e[(b >> 5) + 4096] & (1 << (b & 0x1f))) > 0 )

#define INCR_2B_COUNTER(e,b) \
  { \
     const int w = b >> 5; const u32 m = 1 << (b & 0x1f); \
     const u32 old  = atomic_or(ecounters + w, m) & m; \
     if (old > 0) atomic_or(e + w + 4096, m); \
  }

__attribute__((reqd_work_group_size(128, 1, 1)))
__kernel void FluffySeed2A(__constant u64* nonce, __global ulong4* bufferA, __global ulong4* bufferB, __global u32* indexes)
{
	const int gid = get_global_id(0);
	const int lid = get_local_id(0);

	__global ulong4* buffer;
	__local u64 tmp[64][16];
	__local u32 counters[64];
	u64 sipblock[64];
	u64 v0, v1, v2, v3;

	if (lid < 64) counters[lid] = 0;

	barrier(CLK_LOCAL_MEM_FENCE);

	for (int i = 0; i < 1024 * 2; i += EDGE_BLOCK_SIZE)
	{
		const u64 blockNonce = gid * 2048/*(1024 * 2)*/ + i;

		v0 = nonce[0];
		v1 = nonce[1];
		v2 = nonce[2];
		v3 = nonce[3];

		for (int b = 0; b < EDGE_BLOCK_SIZE; ++b)
		{
			v3 ^= blockNonce + b;
			for (int r = 0; r < 2; ++r) SIPROUND;
			v0 ^= blockNonce + b;
			v2 ^= 0xff;
			for (int r = 0; r < 4; ++r) SIPROUND;
			sipblock[b] = (v0 ^ v1) ^ (v2 ^ v3);
		}

		const u64 last = sipblock[EDGE_BLOCK_MASK];

		for (int s = 0; s < EDGE_BLOCK_SIZE; ++s)
		{
			const u64 lookup = (s == EDGE_BLOCK_MASK) ? last : sipblock[s] ^ last;
			const uint2 hash = (uint2)(lookup & EDGEMASK, (lookup >> 32) & EDGEMASK);
			const int bucket = hash.x & 63;

			//barrier(CLK_LOCAL_MEM_FENCE);

			const int counter = atomic_add(counters + bucket, 1u);
			const int counterLocal = counter % 16;
			tmp[bucket][counterLocal] = hash.x | ((u64)hash.y << 32);

			barrier(CLK_LOCAL_MEM_FENCE);

			if ((counter > 0) && (counterLocal == 0 || counterLocal == 8))
			{
				const int cnt = min((int)atomic_add(indexes + bucket, 8), (DUCK_A_EDGES_64 - 8));
				const int idx = ((bucket < 32 ? bucket : bucket - 32) * DUCK_A_EDGES_64 + cnt) / 4;
				buffer = bucket < 32 ? bufferA : bufferB;

				vstore4(vload4(0, &tmp[bucket][8 - counterLocal]), idx, (__global ulong*)buffer);
				vstore4(vload4(0, &tmp[bucket][12 - counterLocal]), (idx + 1), (__global ulong*)buffer);

				for (uchar i = 0; i < 8; ++i)
					tmp[bucket][8 + i - counterLocal] = 0;
			}
		}
	}

	barrier(CLK_LOCAL_MEM_FENCE);

	if (lid < 64)
	{
		const int counterBase = (counters[lid] % 16) >= 8 ? 8 : 0;
		const int cnt = min((int)atomic_add(indexes + lid, 8), (DUCK_A_EDGES_64 - 8));
		const int idx = ((lid < 32 ? lid : lid - 32) * DUCK_A_EDGES_64 + cnt) / 4;
		buffer = lid < 32 ? bufferA : bufferB;
		buffer[idx] = (ulong4)(tmp[lid][counterBase], tmp[lid][counterBase + 1], tmp[lid][counterBase + 2], tmp[lid][counterBase + 3]);
		buffer[idx + 1] = (ulong4)(tmp[lid][counterBase + 4], tmp[lid][counterBase + 5], tmp[lid][counterBase + 6], tmp[lid][counterBase + 7]);
	}
}

__attribute__((reqd_work_group_size(128, 1, 1)))
__kernel void FluffySeed2B(const __global uint2* source, __global ulong4* destination1, __global ulong4* destination2, const __global int* sourceIndexes, __global int* destinationIndexes, int startBlock)
{
	const int lid = get_local_id(0);
	const int group = get_group_id(0);

	__global ulong4* destination = destination1;
	__local u64 tmp[64][16];
	__local int counters[64];

	if (lid < 64) counters[lid] = 0;

	barrier(CLK_LOCAL_MEM_FENCE);

	int offsetMem = startBlock * DUCK_A_EDGES_64;
	int offsetBucket = 0;
	const int myBucket = group / BKTGRAN;
	const int microBlockNo = group % BKTGRAN;
	const int bucketEdges = min(sourceIndexes[myBucket + startBlock], (DUCK_A_EDGES_64));
	const int microBlockEdgesCount = (DUCK_A_EDGES_64 / BKTGRAN);
	const int loops = (microBlockEdgesCount / 128);

	if ((startBlock == 32) && (myBucket >= 30))
	{
		offsetMem = 0;
		destination = destination2;
		offsetBucket = 30;
	}

	for (int i = 0; i < loops; ++i)
	{
		const int edgeIndex = (microBlockNo * microBlockEdgesCount) + (128 * i) + lid;
		const uint2 edge = source[/*offsetMem + */(myBucket * DUCK_A_EDGES_64) + edgeIndex];
		const bool skip = (edgeIndex >= bucketEdges) || (edge.x == 0 && edge.y == 0);
		const int bucket = (edge.x >> 6) & 63/*(64 - 1)*/;

		//barrier(CLK_LOCAL_MEM_FENCE);

		int counter = 0;
		int counterLocal = 0;

		if (!skip)
		{
			counter = atomic_add(counters + bucket, 1u);
			counterLocal = counter % 16;
			tmp[bucket][counterLocal] = edge.x | ((u64)edge.y << 32);
		}

		barrier(CLK_LOCAL_MEM_FENCE);

		if ((counter > 0) && (counterLocal == 0 || counterLocal == 8))
		{
			const int cnt = min(atomic_add(destinationIndexes + startBlock * 64 + myBucket * 64 + bucket, 8), (DUCK_A_EDGES - 8));
			const int idx = (offsetMem + (((myBucket - offsetBucket) * 64 + bucket) * DUCK_A_EDGES + cnt)) / 4;

			vstore4(vload4(0, &tmp[bucket][8 - counterLocal]), idx, (__global ulong*)destination);
			vstore4(vload4(0, &tmp[bucket][12 - counterLocal]), (idx + 1), (__global ulong*)destination);

			for (uchar i = 0; i < 8; ++i)
				tmp[bucket][8 + i - counterLocal] = 0;
		}
	}

	barrier(CLK_LOCAL_MEM_FENCE);

	if (lid < 64)
	{
		const int counterBase = (counters[lid] % 16) >= 8 ? 8 : 0;
		const int cnt = min(atomic_add(destinationIndexes + startBlock * 64 + myBucket * 64 + lid, 8), (DUCK_A_EDGES - 8));
		const int idx = (offsetMem + (((myBucket - offsetBucket) * 64 + lid) * DUCK_A_EDGES + cnt)) / 4;
		destination[idx] = (ulong4)(tmp[lid][counterBase], tmp[lid][counterBase + 1], tmp[lid][counterBase + 2], tmp[lid][counterBase + 3]);
		destination[idx + 1] = (ulong4)(tmp[lid][counterBase + 4], tmp[lid][counterBase + 5], tmp[lid][counterBase + 6], tmp[lid][counterBase + 7]);
	}
}

__attribute__((reqd_work_group_size(1024, 1, 1)))
__kernel void FluffyRound1(const __global uint2* source1, const __global uint2* source2, __global uint2* destination, const __global int* sourceIndexes, __global int* destinationIndexes, const int bktInSize, const int bktOutSize)
{
	const int lid = get_local_id(0);
	const int group = get_group_id(0);

	const __global uint2* source = (group < 3968/*(62 * 64)*/) ? source1 : source2;
	int groupRead = (group < 3968/*(62 * 64)*/) ? group : group - 3968/*(62 * 64)*/;

	__local u32 ecounters[8192];

	const int edgesInBucket = min(sourceIndexes[group], bktInSize);
	const int loops = (edgesInBucket + CTHREADS) / CTHREADS;

	for (int i = 0; i < 8; ++i)
		ecounters[lid + (1024 * i)] = 0;

	barrier(CLK_LOCAL_MEM_FENCE);

	for (int i = 0; i < loops; ++i)
	{
		const int lindex = (i * CTHREADS) + lid;

		if (lindex < edgesInBucket)
		{
			const uint2 edge = source[(bktInSize * groupRead) + lindex];

			if (edge.x > 0 || edge.y > 0)
				INCR_2B_COUNTER(ecounters, (edge.x & EDGEMASK) >> 12);
		}
	}

	barrier(CLK_LOCAL_MEM_FENCE);

	for (int i = 0; i < loops; ++i)
	{
		const int lindex = (i * CTHREADS) + lid;

		if (lindex < edgesInBucket)
		{
			const uint2 edge = source[(bktInSize * groupRead) + lindex];

			if (edge.x > 0 || edge.y > 0)
				if (READ_2B_COUNTER(ecounters, (edge.x & EDGEMASK) >> 12))
				{
					const int bucket = edge.y & BKTMASK4K;
					const int bktIdx = min(atomic_add(destinationIndexes + bucket, 1), bktOutSize - 1);
					destination[(bucket * bktOutSize) + bktIdx] = (uint2)(edge.y, edge.x);
				}
		}
	}
}

__attribute__((reqd_work_group_size(1024, 1, 1)))
__kernel void FluffyRoundN(const __global uint2* source, __global uint2* destination, const __global int* sourceIndexes, __global int* destinationIndexes)
{
	const int lid = get_local_id(0);
	const int group = get_group_id(0);

	const int bktInSize = DUCK_B_EDGES;
	const int bktOutSize = DUCK_B_EDGES;

	__local u32 ecounters[8192];

	const int edgesInBucket = min(sourceIndexes[group], bktInSize);
	const int loops = (edgesInBucket + CTHREADS) / CTHREADS;

	for (int i = 0; i < 8; ++i)
		ecounters[lid + (1024 * i)] = 0;

	barrier(CLK_LOCAL_MEM_FENCE);

	for (int i = 0; i < loops; ++i)
	{
		const int lindex = (i * CTHREADS) + lid;

		if (lindex < edgesInBucket)
		{
			const uint2 edge = source[(bktInSize * group) + lindex];

			if (edge.x > 0 || edge.y > 0)
				INCR_2B_COUNTER(ecounters, (edge.x & EDGEMASK) >> 12);
		}
	}

	barrier(CLK_LOCAL_MEM_FENCE);

	for (int i = 0; i < loops; ++i)
	{
		const int lindex = (i * CTHREADS) + lid;

		if (lindex < edgesInBucket)
		{
			const uint2 edge = source[(bktInSize * group) + lindex];

			if (edge.x > 0 || edge.y > 0)
				if (READ_2B_COUNTER(ecounters, (edge.x & EDGEMASK) >> 12))
				{
					const int bucket = edge.y & BKTMASK4K;
					const int bktIdx = min(atomic_add(destinationIndexes + bucket, 1), bktOutSize - 1);
					destination[(bucket * bktOutSize) + bktIdx] = (uint2)(edge.y, edge.x);
				}
		}
	}
}

__attribute__((reqd_work_group_size(64, 1, 1)))
__kernel void FluffyRoundN_64(const __global uint2* source, __global uint2* destination, const __global int* sourceIndexes, __global int* destinationIndexes)
{
	const int lid = get_local_id(0);
	const int group = get_group_id(0);

	const int bktInSize = DUCK_B_EDGES;
	const int bktOutSize = DUCK_B_EDGES;

	__local u32 ecounters[8192];

	const int edgesInBucket = min(sourceIndexes[group], bktInSize);
	const int loops = (edgesInBucket + 64) / 64;

	for (int i = 0; i < 128/*8 * 16*/; ++i)
		ecounters[lid + (64 * i)] = 0;

	barrier(CLK_LOCAL_MEM_FENCE);

	for (int i = 0; i < loops; ++i)
	{
		const int lindex = (i * 64) + lid;

		if (lindex < edgesInBucket)
		{
			const uint2 edge = source[(bktInSize * group) + lindex];

			if (edge.x > 0 || edge.y > 0)
				INCR_2B_COUNTER(ecounters, (edge.x & EDGEMASK) >> 12);
		}
	}

	barrier(CLK_LOCAL_MEM_FENCE);

	for (int i = 0; i < loops; ++i)
	{
		const int lindex = (i * 64) + lid;

		if (lindex < edgesInBucket)
		{
			const uint2 edge = source[(bktInSize * group) + lindex];

			if (edge.x > 0 || edge.y > 0)
				if (READ_2B_COUNTER(ecounters, (edge.x & EDGEMASK) >> 12))
				{
					const int bucket = edge.y & BKTMASK4K;
					const int bktIdx = min(atomic_add(destinationIndexes + bucket, 1), bktOutSize - 1);
					destination[(bucket * bktOutSize) + bktIdx] = (uint2)(edge.y, edge.x);
				}
		}
	}
}

__attribute__((reqd_work_group_size(1024, 1, 1)))
__kernel void FluffyTail(const __global uint2* source, __global uint2* destination, const __global int* sourceIndexes, __global int* destinationIndexes)
{
	const int lid = get_local_id(0);
	const int group = get_group_id(0);

	const int myEdges = sourceIndexes[group];
	__local int destIdx;

	if (lid == 0) destIdx = atomic_add(destinationIndexes, myEdges);

	barrier(CLK_LOCAL_MEM_FENCE);

	if (lid < myEdges) destination[destIdx + lid] = source[group * DUCK_B_EDGES + lid];
}

__attribute__((reqd_work_group_size(256, 1, 1)))
__kernel void FluffyRecovery(__constant u64* nonce, const __constant u64 * recovery, __global int* indexes)
{
	const int gid = get_global_id(0);
	const int lid = get_local_id(0);

	__local u32 nonces[42];
	u64 sipblock[64];
	u64 v0, v1, v2, v3;

	if (lid < 42) nonces[lid] = 0;

	barrier(CLK_LOCAL_MEM_FENCE);

	for (int i = 0; i < 1024; i += EDGE_BLOCK_SIZE)
	{
		const u64 blockNonce = gid * 1024 + i;

		v0 = nonce[0];
		v1 = nonce[1];
		v2 = nonce[2];
		v3 = nonce[3];

		for (u32 b = 0; b < EDGE_BLOCK_SIZE; ++b)
		{
			v3 ^= blockNonce + b;
			for (int r = 0; r < 2; ++r) SIPROUND;
			v0 ^= blockNonce + b;
			v2 ^= 0xff;
			for (int r = 0; r < 4; ++r) SIPROUND;
			sipblock[b] = (v0 ^ v1) ^ (v2 ^ v3);
		}

		const u64 last = sipblock[EDGE_BLOCK_MASK];

		for (int s = EDGE_BLOCK_MASK; s >= 0; s--)
		{
			const u64 lookup = (s == EDGE_BLOCK_MASK) ? last : sipblock[s] ^ last;
			const u64 u = lookup & EDGEMASK;
			const u64 v = (lookup >> 32) & EDGEMASK;

			const u64 a = u | (v << 32);
			const u64 b = v | (u << 32);

			for (int i = 0; i < 42; ++i)
				if ((recovery[i] == a) || (recovery[i] == b))
					nonces[i] = blockNonce + s;
		}
	}

	barrier(CLK_LOCAL_MEM_FENCE);

	if (lid < 42)
		if (nonces[lid] > 0)
			indexes[lid] = nonces[lid];
}

// ---------------
#define BKT_OFFSET 255
#define BKT_STEP 32

__attribute__((reqd_work_group_size(1024, 1, 1)))
__kernel void FluffyRoundNO1(const __global uint2* source, __global uint2* destination, const __global int* sourceIndexes, __global int* destinationIndexes)
{
	const int lid = get_local_id(0);
	const int group = get_group_id(0);

	const int bktInSize = DUCK_B_EDGES;
	const int bktOutSize = DUCK_B_EDGES;

	__local u32 ecounters[8192];

	const int edgesInBucket = min(sourceIndexes[group], bktInSize);
	const int loops = (edgesInBucket + CTHREADS) / CTHREADS;

	for (int i = 0; i < 8; ++i)
		ecounters[lid + (1024 * i)] = 0;

	barrier(CLK_LOCAL_MEM_FENCE);

	for (int i = 0; i < loops; ++i)
	{
		const int lindex = (i * CTHREADS) + lid;

		if (lindex < edgesInBucket)
		{
			const uint2 edge = source[(bktInSize * group) + lindex];

			if (edge.x > 0 || edge.y > 0)
				INCR_2B_COUNTER(ecounters, (edge.x & EDGEMASK) >> 12);
		}
	}

	barrier(CLK_LOCAL_MEM_FENCE);

	for (int i = 0; i < loops; ++i)
	{
		const int lindex = (i * CTHREADS) + lid;

		if (lindex < edgesInBucket)
		{
			const uint2 edge = source[(bktInSize * group) + lindex];

			if (edge.x > 0 || edge.y > 0)
				if (READ_2B_COUNTER(ecounters, (edge.x & EDGEMASK) >> 12))
				{
					const int bucket = edge.y & BKTMASK4K;
					const int bktIdx = min(atomic_add(destinationIndexes + bucket, 1), bktOutSize - 1 - ((bucket & BKT_OFFSET) * BKT_STEP));
					destination[((bucket & BKT_OFFSET) * BKT_STEP) + (bucket * bktOutSize) + bktIdx] = (uint2)(edge.y, edge.x);
				}
		}
	}
}

__attribute__((reqd_work_group_size(1024, 1, 1)))
__kernel void FluffyRoundNON(const __global uint2* source, __global uint2* destination, const __global int* sourceIndexes, __global int* destinationIndexes)
{
	const int lid = get_local_id(0);
	const int group = get_group_id(0);

	const int bktInSize = DUCK_B_EDGES;
	const int bktOutSize = DUCK_B_EDGES;

	__local u32 ecounters[8192];

	const int edgesInBucket = min(sourceIndexes[group], bktInSize);
	const int loops = (edgesInBucket + CTHREADS) / CTHREADS;

	for (int i = 0; i < 8; ++i)
		ecounters[lid + (1024 * i)] = 0;

	barrier(CLK_LOCAL_MEM_FENCE);

	for (int i = 0; i < loops; ++i)
	{
		const int lindex = (i * CTHREADS) + lid;

		if (lindex < edgesInBucket)
		{
			const uint2 edge = source[((group & BKT_OFFSET) * BKT_STEP) + (bktInSize * group) + lindex];

			if (edge.x > 0 || edge.y > 0)
				INCR_2B_COUNTER(ecounters, (edge.x & EDGEMASK) >> 12);
		}
	}

	barrier(CLK_LOCAL_MEM_FENCE);

	for (int i = 0; i < loops; ++i)
	{
		const int lindex = (i * CTHREADS) + lid;

		if (lindex < edgesInBucket)
		{
			const uint2 edge = source[((group & BKT_OFFSET) * BKT_STEP) + (bktInSize * group) + lindex];

			if (edge.x > 0 || edge.y > 0)
				if (READ_2B_COUNTER(ecounters, (edge.x & EDGEMASK) >> 12))
				{
					const int bucket = edge.y & BKTMASK4K;
					const int bktIdx = min(atomic_add(destinationIndexes + bucket, 1), bktOutSize - 1 - ((bucket & BKT_OFFSET) * BKT_STEP));
					destination[((bucket & BKT_OFFSET) * BKT_STEP) + (bucket * bktOutSize) + bktIdx] = (uint2)(edge.y, edge.x);
				}
		}
	}
}

__attribute__((reqd_work_group_size(1024, 1, 1)))
__kernel void FluffyTailO(const __global uint2* source, __global uint2* destination, const __global int* sourceIndexes, __global int* destinationIndexes)
{
	const int lid = get_local_id(0);
	const int group = get_group_id(0);

	const int myEdges = sourceIndexes[group];
	__local int destIdx;

	if (lid == 0) destIdx = atomic_add(destinationIndexes, myEdges);

	barrier(CLK_LOCAL_MEM_FENCE);

	if (lid < myEdges) destination[destIdx + lid] = source[((group & BKT_OFFSET) * BKT_STEP) + group * DUCK_B_EDGES + lid];
}
