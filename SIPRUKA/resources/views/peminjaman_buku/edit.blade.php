@extends('layouts.app')

@section('content')
<div class="container mt-5">
    <h1>Edit Peminjaman Buku</h1>
    <a href="{{ route('peminjaman-buku.index') }}" class="btn btn-secondary mb-3">Kembali</a>

        <form action="{{ route('peminjaman-buku.update', $peminjaman->id) }}" method="POST">
            @csrf
            @method('PUT')
            <div class="mb-3">
                <label for="user_id" class="form-label">User</label>
                <select name="user_id" id="user_id" class="form-control" required>
                    <option value="">Pilih User</option>
                    @foreach($users as $user)
                        <option value="{{ $user->id }}" {{ $peminjaman->user_id == $user->id ? 'selected' : '' }}>{{ $user->nama }}</option>
                    @endforeach
                </select>
            </div>

            <div class="mb-3">
                <label for="buku_id" class="form-label">Buku</label>
                <select name="buku_id" id="buku_id" class="form-control" required>
                    <option value="">Pilih Buku</option>
                    @foreach($buku as $b)
                        <option value="{{ $b->id }}" {{ $peminjaman->buku_id == $b->id ? 'selected' : '' }}>{{ $b->judul }}</option>
                    @endforeach
                </select>
            </div>

            <div class="mb-3">
                <label for="tanggal_pinjam" class="form-label">Tanggal Pinjam</label>
                <input type="date" name="tanggal_pinjam" id="tanggal_pinjam" class="form-control" value="{{ $peminjaman->tanggal_pinjam }}" required>
            </div>

            <div class="mb-3">
                <label for="tanggal_kembali" class="form-label">Tanggal Kembali</label>
                <input type="date" name="tanggal_kembali" id="tanggal_kembali" class="form-control" value="{{ $peminjaman->tanggal_kembali }}">
            </div>

            <div class="mb-3">
                <label for="status" class="form-label">Status</label>
                <select name="status" id="status" class="form-control" required>
                    <option value="pending" {{ $peminjaman->status == 'pending' ? 'selected' : '' }}>Pending</option>
                    <option value="dipinjam" {{ $peminjaman->status == 'dipinjam' ? 'selected' : '' }}>Dipinjam</option>
                    <option value="dikembalikan" {{ $peminjaman->status == 'dikembalikan' ? 'selected' : '' }}>Dikembalikan</option>
                </select>
            </div>

            <button type="submit" class="btn btn-primary">Update</button>
        </form>
    </div>
@endsection
