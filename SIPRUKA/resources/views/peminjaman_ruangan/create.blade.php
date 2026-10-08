<x-app-layout>
    <x-slot name="header">
        <h2 class="font-semibold text-xl text-gray-800">
            Tambah Peminjaman Ruangan
        </h2>
    </x-slot>

    <div class="py-6">
        <div class="max-w-3xl mx-auto bg-white shadow p-6 rounded-lg">

            <a href="{{ route('peminjaman-ruangan.index') }}"
               class="bg-gray-200 hover:bg-gray-300 text-gray-800 px-4 py-2 rounded-md inline-block mb-4">
                Kembali
            </a>

            <form action="{{ route('peminjaman-ruangan.store') }}" method="POST" class="space-y-4">
                @csrf

                {{-- User --}}
                <div>
                    <label class="block font-semibold mb-1">User</label>
                    <select name="user_id" class="w-full border p-2 rounded" required>
                        <option value="">Pilih User</option>
                        @foreach ($users as $user)
                            <option value="{{ $user->id }}">{{ $user->nama }}</option>
                        @endforeach
                    </select>
                </div>

                {{-- Ruangan --}}
                <div>
                    <label class="block font-semibold mb-1">Ruangan</label>
                    <select name="ruangan_id" class="w-full border p-2 rounded" required>
                        <option value="">Pilih Ruangan</option>
                        @foreach ($ruangan as $r)
                            <option value="{{ $r->id }}">{{ $r->nama_ruangan }}</option>
                        @endforeach
                    </select>
                </div>

                {{-- Tanggal Mulai --}}
                <div>
                    <label class="block font-semibold mb-1">Tanggal Mulai</label>
                    <input type="date" name="tanggal_mulai" class="w-full border p-2 rounded" required>
                </div>

                {{-- Tanggal Selesai --}}
                <div>
                    <label class="block font-semibold mb-1">Tanggal Selesai</label>
                    <input type="date" name="tanggal_selesai" class="w-full border p-2 rounded" required>
                </div>

                {{-- Status --}}
                <div>
                    <label class="block font-semibold mb-1">Status</label>
                    <select name="status" class="w-full border p-2 rounded" required>
                        <option value="pending">Pending</option>
                        <option value="disetujui">Disetujui</option>
                        <option value="ditolak">Ditolak</option>
                    </select>
                </div>

                <button type="submit"
                        class="bg-blue-600 hover:bg-blue-700 text-white px-4 py-2 rounded">
                    Simpan
                </button>
            </form>

        </div>
    </div>
</x-app-layout>
