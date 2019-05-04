// Cuckaroo Cycle, a memory-hard proof-of-work by John Tromp and team Grin
// Copyright (c) 2018 Jiri Photon Vadura and John Tromp
// Modified work Copyright (c) 2019 Lip Wee Yeo
// This Grinble miner file is covered by the FAIR MINING license

#ifdef __INTELLISENSE__

#ifndef __CUDACC__
#	define __CUDACC__
#endif // !__CUDACC__

#ifndef __CUDA_ARCH__
#	define __CUDA_ARCH__ 610
#endif // !__CUDA_ARCH__

#include <device_launch_parameters.h>
#include <device_atomic_functions.h>
#include <device_functions.h>
#include <sm_20_intrinsics.h>
#include <vector_functions.h>
#include <math_functions.h>
#include <host_defines.h>

#ifdef __constant__
#	undef __constant__
#endif // __constant__
#define __constant__

#ifdef __shared__
#	undef __shared__
#endif // __shared__
#define __shared__

#ifdef __global__
#	undef __global__
#endif // __global__
#define __global__

#ifdef __device__
#	undef __device__
#endif // __device__
#define __device__

#endif // __INTELLISENSE__

typedef unsigned char u8;
typedef unsigned short u16;
typedef unsigned int u32;
typedef unsigned long long u64;

#define DUCK_SIZE_A 129
#define DUCK_SIZE_B 82

#define DUCK_A_EDGES (DUCK_SIZE_A * 1024)
#define DUCK_A_EDGES_64 (DUCK_A_EDGES * 64)

#define DUCK_B_EDGES (DUCK_SIZE_B * 1024)
#define DUCK_B_EDGES_64 (DUCK_B_EDGES * 64)

#define EDGE_BLOCK_SIZE 64
#define EDGE_BLOCK_MASK (EDGE_BLOCK_SIZE - 1)

#define EDGEBITS 29
#define NEDGES (1u << EDGEBITS)
#define EDGEMASK (NEDGES - 1)

#define CTHREADS 512
#define BKTMASK4K (4096-1)
#define BKTGRAN 32

#define ROTL(x,b) ( ((x) << (b)) | ( (x) >> (64 - (b))) )

#define SIPROUND \
  { \
    v0 += v1; v2 += v3; v1 = ROTL(v1,13); \
    v3 = ROTL(v3,16); v1 ^= v0; v3 ^= v2; \
    v0 = ROTL(v0,32); v2 += v1; v0 += v3; \
    v1 = ROTL(v1,17); v3 = ROTL(v3,21); \
    v1 ^= v2; v3 ^= v0; v2 = ROTL(v2,32); \
  }

#define READ_2B_COUNTER(e,b) ( (e[(b >> 5) + 4096] & (1 << (b & 0x1f))) > 0 )

#define INCR_2B_COUNTER(e,b) \
  { \
    const int w = b >> 5; const u32 m = 1 << (b & 0x1f); \
    const u32 old  = atomicOr(ecounters + w, m) & m; \
    if (old > 0) atomicOr(e + w + 4096, m); \
  }

extern "C" {

	__constant__ u64 nonce[4];
	__constant__ u64 recovery[42];

	__global__ void FluffySeed2A(ulonglong4* buffer, int* indexes)
	{
		const u32 gid = blockDim.x * blockIdx.x + threadIdx.x;
		const u32 lid = threadIdx.x;

		__shared__ u64 tmp[64][16];
		__shared__ u32 counters[64];

		u64 sipblock[64];
		u64 v0, v1, v2, v3;

		if (lid < 64) counters[lid] = 0;

		__syncthreads();

		for (int i = 0; i < 1024 * 2; i += EDGE_BLOCK_SIZE)
		{
			const u64 blockNonce = gid * (1024 * 2) + i;

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
				const uint2 hash = make_uint2(lookup & EDGEMASK, (lookup >> 32) & EDGEMASK);
				const int bucket = hash.x & 63;

				__syncthreads();

				const int counter = atomicAdd(counters + bucket, 1u);
				const int counterLocal = counter % 16;
				tmp[bucket][counterLocal] = hash.x | ((u64)hash.y << 32);

				__syncthreads();

				if ((counter > 0) && (counterLocal == 0 || counterLocal == 8))
				{
					const int cnt = min(atomicAdd(indexes + bucket, 8), (DUCK_A_EDGES_64 - 8));
					const int idx = (bucket * DUCK_A_EDGES_64 + cnt) / 4;

					buffer[idx] = make_ulonglong4(
						atomicExch(&tmp[bucket][8 - counterLocal], 0),
						atomicExch(&tmp[bucket][9 - counterLocal], 0),
						atomicExch(&tmp[bucket][10 - counterLocal], 0),
						atomicExch(&tmp[bucket][11 - counterLocal], 0)
					);
					buffer[idx + 1] = make_ulonglong4(
						atomicExch(&tmp[bucket][12 - counterLocal], 0),
						atomicExch(&tmp[bucket][13 - counterLocal], 0),
						atomicExch(&tmp[bucket][14 - counterLocal], 0),
						atomicExch(&tmp[bucket][15 - counterLocal], 0)
					);
				}
			}
		}

		__syncthreads();

		if (lid < 64)
		{
			const int counterBase = (counters[lid] % 16) >= 8 ? 8 : 0;
			const int cnt = min(atomicAdd(indexes + lid, 8), (DUCK_A_EDGES_64 - 8));
			const int idx = (lid * DUCK_A_EDGES_64 + cnt) / 4;
			buffer[idx] = make_ulonglong4(tmp[lid][counterBase], tmp[lid][counterBase + 1], tmp[lid][counterBase + 2], tmp[lid][counterBase + 3]);
			buffer[idx + 1] = make_ulonglong4(tmp[lid][counterBase + 4], tmp[lid][counterBase + 5], tmp[lid][counterBase + 6], tmp[lid][counterBase + 7]);
		}
	}

	__global__ void FluffySeed2B(const uint2* source, ulonglong4* destination, const int* sourceIndexes, int* destinationIndexes, int startBlock)
	{
		const u32 lid = threadIdx.x;
		const u32 group = blockIdx.x;

		__shared__ u64 tmp[64][16];
		__shared__ int counters[64];

		if (lid < 64) counters[lid] = 0;

		__syncthreads();

		const int offsetMem = startBlock * DUCK_A_EDGES_64;
		const int myBucket = group / BKTGRAN;
		const int microBlockNo = group % BKTGRAN;
		const int bucketEdges = min(sourceIndexes[myBucket + startBlock], (DUCK_A_EDGES_64));
		const int microBlockEdgesCount = (DUCK_A_EDGES_64 / BKTGRAN);
		const int loops = (microBlockEdgesCount / 128);

		for (int i = 0; i < loops; ++i)
		{
			const int edgeIndex = (microBlockNo * microBlockEdgesCount) + (128 * i) + lid;
			const uint2 edge = source[offsetMem + (myBucket * DUCK_A_EDGES_64) + edgeIndex];
			const bool skip = (edgeIndex >= bucketEdges) || (edge.x == 0 && edge.y == 0);
			const int bucket = (edge.x >> 6) & (64 - 1);

			__syncthreads();

			const int counter = skip ? 0 : atomicAdd(counters + bucket, 1u);
			const int counterLocal = skip ? 0 : counter % 16;
			tmp[bucket][counterLocal] = edge.x | ((u64)edge.y << 32);

			__syncthreads();

			if ((counter > 0) && (counterLocal == 0 || counterLocal == 8))
			{
				const int cnt = min(atomicAdd(destinationIndexes + startBlock * 64 + myBucket * 64 + bucket, 8), (DUCK_A_EDGES - 8));
				const int idx = ((myBucket * 64 + bucket) * DUCK_A_EDGES + cnt) / 4;

				destination[idx] = make_ulonglong4(
					atomicExch(&tmp[bucket][8 - counterLocal], 0),
					atomicExch(&tmp[bucket][9 - counterLocal], 0),
					atomicExch(&tmp[bucket][10 - counterLocal], 0),
					atomicExch(&tmp[bucket][11 - counterLocal], 0)
				);
				destination[idx + 1] = make_ulonglong4(
					atomicExch(&tmp[bucket][12 - counterLocal], 0),
					atomicExch(&tmp[bucket][13 - counterLocal], 0),
					atomicExch(&tmp[bucket][14 - counterLocal], 0),
					atomicExch(&tmp[bucket][15 - counterLocal], 0)
				);
			}
		}

		__syncthreads();

		if (lid < 64)
		{
			const int counterBase = (counters[lid] % 16) >= 8 ? 8 : 0;
			const int cnt = min(atomicAdd(destinationIndexes + startBlock * 64 + myBucket * 64 + lid, 8), (DUCK_A_EDGES - 8));
			const int idx = ((myBucket * 64 + lid) * DUCK_A_EDGES + cnt) / 4;
			destination[idx] = make_ulonglong4(tmp[lid][counterBase], tmp[lid][counterBase + 1], tmp[lid][counterBase + 2], tmp[lid][counterBase + 3]);
			destination[idx + 1] = make_ulonglong4(tmp[lid][counterBase + 4], tmp[lid][counterBase + 5], tmp[lid][counterBase + 6], tmp[lid][counterBase + 7]);
		}
	}

	__global__ void FluffyRound(const uint2* source, uint2* destination, const int* sourceIndexes, int* destinationIndexes, const int bktInSize, const int bktOutSize)
	{
		const u32 lid = threadIdx.x;
		const u32 group = blockIdx.x;

		__shared__ u32 ecounters[8192];

		const int bktInGroupSize = bktInSize * group;
		const int edgesInBucket = min(sourceIndexes[group], bktInSize);
		const int loops = (edgesInBucket + CTHREADS) / CTHREADS;

		for (int i = 0; i < 16; ++i)
			ecounters[lid + (512 * i)] = 0;

		__syncthreads();

		for (int i = 0; i < loops; ++i)
		{
			const int lindex = (i * CTHREADS) + lid;

			if (lindex < edgesInBucket)
			{
				const uint2 edge = __ldg(&source[bktInGroupSize + lindex]);

				if (edge.x > 0 || edge.y > 0)
					INCR_2B_COUNTER(ecounters, (edge.x & EDGEMASK) >> 12);
			}
		}

		__syncthreads();

		for (int i = 0; i < loops; ++i)
		{
			const int lindex = (i * CTHREADS) + lid;

			if (lindex < edgesInBucket)
			{
				const uint2 edge = __ldg(&source[bktInGroupSize + lindex]);

				if (edge.x > 0 || edge.y > 0)
					if (READ_2B_COUNTER(ecounters, (edge.x & EDGEMASK) >> 12))
					{
						const int bucket = edge.y & BKTMASK4K;
						const int bktIdx = min(atomicAdd(destinationIndexes + bucket, 1), bktOutSize - 1);
						destination[(bucket * bktOutSize) + bktIdx] = make_uint2(edge.y, edge.x);
					}
			}
		}
	}

	__global__ void FluffyRound_J(const uint2* sourceA, const uint2* sourceB, uint2* destination, const int* sourceIndexes, int* destinationIndexes, const int bktInSize, const int bktOutSize)
	{
		const u32 lid = threadIdx.x;
		const u32 group = blockIdx.x;

		__shared__ u32 ecounters[8192];

		const int bktInGroupSize = bktInSize * group;
		const int edgesInBucketA = min(sourceIndexes[group], bktInSize);
		const int edgesInBucketB = min(sourceIndexes[group + 4096], bktInSize);

		const int loopsA = (edgesInBucketA + CTHREADS) / CTHREADS;
		const int loopsB = (edgesInBucketB + CTHREADS) / CTHREADS;

		for (int i = 0; i < 16; ++i)
			ecounters[lid + (512 * i)] = 0;

		__syncthreads();

		for (int i = 0; i < loopsA; ++i)
		{
			const int lindex = (i * CTHREADS) + lid;

			if (lindex < edgesInBucketA)
			{
				const uint2 edge = sourceA[bktInGroupSize + lindex];

				if (edge.x > 0 || edge.y > 0)
					INCR_2B_COUNTER(ecounters, (edge.x & EDGEMASK) >> 12);
			}
		}

		for (int i = 0; i < loopsB; ++i)
		{
			const int lindex = (i * CTHREADS) + lid;

			if (lindex < edgesInBucketB)
			{
				const uint2 edge = sourceB[bktInGroupSize + lindex];

				if (edge.x > 0 || edge.y > 0)
					INCR_2B_COUNTER(ecounters, (edge.x & EDGEMASK) >> 12);
			}
		}

		__syncthreads();

		for (int i = 0; i < loopsA; ++i)
		{
			const int lindex = (i * CTHREADS) + lid;

			if (lindex < edgesInBucketA)
			{
				const uint2 edge = sourceA[bktInGroupSize + lindex];

				if (edge.x > 0 || edge.y > 0)
					if (READ_2B_COUNTER(ecounters, (edge.x & EDGEMASK) >> 12))
					{
						const int bucket = edge.y & BKTMASK4K;
						const int bktIdx = min(atomicAdd(destinationIndexes + bucket, 1), bktOutSize - 1);
						destination[(bucket * bktOutSize) + bktIdx] = make_uint2(edge.y, edge.x);
					}
			}
		}

		for (int i = 0; i < loopsB; ++i)
		{
			const int lindex = (i * CTHREADS) + lid;

			if (lindex < edgesInBucketB)
			{
				const uint2 edge = sourceB[bktInGroupSize + lindex];

				if (edge.x > 0 || edge.y > 0)
					if (READ_2B_COUNTER(ecounters, (edge.x & EDGEMASK) >> 12))
					{
						const int bucket = edge.y & BKTMASK4K;
						const int bktIdx = min(atomicAdd(destinationIndexes + bucket, 1), bktOutSize - 1);
						destination[(bucket * bktOutSize) + bktIdx] = make_uint2(edge.y, edge.x);
					}
			}
		}
	}

	__global__ void FluffyTail(const uint2* source, uint2* destination, const int* sourceIndexes, int* destinationIndexes)
	{
		const u32 lid = threadIdx.x;
		const u32 group = blockIdx.x;

		const int myEdges = sourceIndexes[group];
		__shared__ int destIdx;

		if (lid == 0) destIdx = atomicAdd(destinationIndexes, myEdges);

		__syncthreads();

		if (lid < myEdges) destination[destIdx + lid] = source[group * DUCK_B_EDGES / 4 + lid];
	}

	__global__ void FluffyRecovery(int* indexes)
	{
		const u32 gid = blockDim.x * blockIdx.x + threadIdx.x;
		const u32 lid = threadIdx.x;

		__shared__ u32 nonces[42];

		u64 sipblock[64];
		u64 v0, v1, v2, v3;

		if (lid < 42) nonces[lid] = 0;

		__syncthreads();

		for (int i = 0; i < 1024; i += EDGE_BLOCK_SIZE)
		{
			const u64 blockNonce = gid * 1024 + i;

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

			for (int s = EDGE_BLOCK_MASK; s >= 0; --s)
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

		__syncthreads();

		if (lid < 42)
			if (nonces[lid] > 0)
				indexes[lid] = nonces[lid];
	}
}
