<?php

namespace App\Http\Controllers;

use App\Models\PeminjamanRuangan;
use App\Models\Ruangan;
use App\Models\User;
use Illuminate\Http\Request;

class PeminjamanRuanganController extends Controller
{
    public function index()
    {
        $data = PeminjamanRuangan::with(['user', 'ruangan'])->get();
        return view('peminjaman_ruangan.index', compact('data'));
    }

    public function create()
    {
        return view('peminjaman_ruangan.create', [
            'users' => User::all(),
            'ruangan' => Ruangan::all()
        ]);
    }

    public function store(Request $request)
    {
        $request->validate([
            'user_id' => 'required',
            'ruangan_id' => 'required',
            'tanggal_mulai' => 'required|date',
            'tanggal_selesai' => 'required|date|after_or_equal:tanggal_mulai',
        ]);

        PeminjamanRuangan::create([
            'user_id' => $request->user_id,
            'ruangan_id' => $request->ruangan_id,
            'tanggal_mulai' => $request->tanggal_mulai,
            'tanggal_selesai' => $request->tanggal_selesai,
            'status' => 'pending',
        ]);

        return redirect()->route('peminjaman-ruangan.index')->with('success', 'Peminjaman ruangan berhasil dibuat');
    }

    public function edit(string $id)
    {
        $peminjaman = PeminjamanRuangan::findOrFail($id);
        return view('peminjaman_ruangan.edit', [
            'peminjaman' => $peminjaman,
            'users' => User::all(),
            'ruangan' => Ruangan::all()
        ]);
    }

    public function update(Request $request, string $id)
    {
        $request->validate([
            'user_id' => 'required',
            'ruangan_id' => 'required',
            'tanggal_mulai' => 'required|date',
            'tanggal_selesai' => 'required|date|after_or_equal:tanggal_mulai',
            'status' => 'required|in:pending,disetujui,ditolak',
        ]);

        $peminjaman = PeminjamanRuangan::findOrFail($id);
        $peminjaman->update($request->all());

        return redirect()->route('peminjaman-ruangan.index')->with('success', 'Peminjaman ruangan berhasil diperbarui');
    }

    public function destroy(string $id)
    {
        $peminjaman = PeminjamanRuangan::findOrFail($id);
        $peminjaman->delete();

        return redirect()->route('peminjaman-ruangan.index')->with('success', 'Peminjaman ruangan berhasil dihapus');
    }
}
