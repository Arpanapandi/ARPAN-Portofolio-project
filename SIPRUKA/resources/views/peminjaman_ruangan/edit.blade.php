<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Edit Peminjaman Ruangan</title>
    <link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css" rel="stylesheet">
</head>
<body>
    <div class="container mt-5">
        <h1>Edit Peminjaman Ruangan</h1>
        <a href="{{ route('peminjaman-ruangan.index') }}" class="btn btn-secondary mb-3">Kembali</a>

        <form action="{{ route('peminjaman-ruangan.update', $peminjaman->id) }}" method="POST">
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
                <label for="ruangan_id" class="form-label">Ruangan</label>
                <select name="ruangan_id" id="ruangan_id" class="form-control" required>
                    <option value="">Pilih Ruangan</option>
                    @foreach($ruangan as $r)
                        <option value="{{ $r->id }}" {{ $peminjaman->ruangan_id == $r->id ? 'selected' : '' }}>{{ $r->nama }}</option>
                    @endforeach
                </select>
            </div>

            <div class="mb-3">
                <label for="tanggal_mulai" class="form-label">Tanggal Mulai</label>
                <input type="date" name="tanggal_mulai" id="tanggal_mulai" class="form-control" value="{{ $peminjaman->tanggal_mulai }}" required>
            </div>

            <div class="mb-3">
                <label for="tanggal_selesai" class="form-label">Tanggal Selesai</label>
                <input type="date" name="tanggal_selesai" id="tanggal_selesai" class="form-control" value="{{ $peminjaman->tanggal_selesai }}" required>
            </div>

            <div class="mb-3">
                <label for="status" class="form-label">Status</label>
                <select name="status" id="status" class="form-control" required>
                    <option value="pending" {{ $peminjaman->status == 'pending' ? 'selected' : '' }}>Pending</option>
                    <option value="disetujui" {{ $peminjaman->status == 'disetujui' ? 'selected' : '' }}>Disetujui</option>
                    <option value="ditolak" {{ $peminjaman->status == 'ditolak' ? 'selected' : '' }}>Ditolak</option>
                </select>
            </div>

            <button type="submit" class="btn btn-primary">Update</button>
        </form>
    </div>
</body>
</html>
